using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: A-121, fires as obstacles (FireSpots). Walkable turns down a fire's circle and a lava pool's
        // shallow rim; Advance slides a walker round a fire like a wall. Row 2: a lit fire's circle is widened by the
        // creature's own berth (CreatureMind.FireKeep: shyness, or heat it can't bear), set at each comfort check,
        // where one standing too close backs off and a fire-lover drifts back to one.
        partial struct BrainJob
        {
            /// <summary>Clearance (m) kept between a body and a fire's flames.</summary>
            const float FireClearance = 0.1f;

            /// <summary>
            /// In a fire's flames (padded a little, for a spot to stand or a step ahead), or on the shallow rim of a lava
            /// pool, where the ground is a hand's depth under molten rock. <paramref name="depth"/> is the water's depth
            /// in <paramref name="g"/> (negative on dry ground).
            /// </summary>
            bool InFireOrLava(float2 xz, in GroundSample g, float depth, bool flames = true)
            {
                if (flames && Fires.On && Fires.Near(xz, 0.2f, M.FireKeep, Night, out _))
                    return true;
                if (!g.HasWater || depth <= -0.3f)
                    return false;
                Terrain.WaterLook(xz, g.Biomes, out var glow);
                return glow.y > 0.5f;
            }

            /// <summary>
            /// The look-ahead hit a fire and nothing else: rather than stop or give up its walk (as at a cliff), it
            /// turns along the fire's edge on the side away from the flames (head-on, its own side) and keeps going, so
            /// it walks round. False when the way ahead is shut for some other reason too.
            /// </summary>
            bool SteerRoundFire(ref CreatureState s, ref CreatureBlob b, float2 forward, float2 ahead, float dt)
            {
                if (!Fires.On || !Fires.Near(ahead, 0.2f, M.FireKeep, Night, out var fire) || !Walkable(ahead, b.Habitat, false))
                    return false;
                var to = fire.Position.xz - s.Position.xz;
                var left = new float2(-forward.y, forward.x);
                var lean = math.dot(to, left);
                var away = math.abs(lean) > 0.05f ? -math.sign(lean) : (s.Seed & 1u) == 0u ? 1f : -1f;
                // The tangent to the fire's circle, on the far side from the flames.
                var dir = math.normalizesafe(to, forward);
                var tangent = new float2(-dir.y, dir.x) * away;
                var want = math.atan2(tangent.x, tangent.y);
                s.Yaw = WrapAngle(s.Yaw + WrapAngle(want - s.Yaw) * math.min(1f, dt * 6f));
                return true;
            }

            /// <summary>
            /// A step from where it stands to <paramref name="to"/> that would put its body in a fire is turned to the
            /// nearest open way either side (turning it a little that way), as at a wall; with nowhere to go it stops.
            /// One already in a fire (spawned there, or a forest flame that caught under it) walks straight out. A target
            /// inside a fire is moved to its edge, so it isn't circled for ever.
            /// </summary>
            float2 KeepOutOfFire(ref CreatureState s, ref CreatureBlob b, float2 to, float dt)
            {
                var pad = BodyRadius(s, ref b) + FireClearance;
                if (Fires.Near(s.Position.xz, pad, M.FireKeep, Night, out var here))
                {
                    var away = s.Position.xz - here.Position.xz;
                    var length = math.length(away);
                    var dir = length > 1e-3f ? away / length : -new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                    var edge = here.Position.xz + dir * (Berth(in here, pad) + 0.02f);
                    var push = math.min(math.distance(edge, s.Position.xz), math.max(s.Speed, 1f) * dt + 0.02f);
                    s.Yaw = math.atan2(dir.x, dir.y);
                    return s.Position.xz + dir * push;
                }
                if (Fires.Near(s.Target, pad, M.FireKeep, Night, out var round))
                {
                    var off = s.Target - round.Position.xz;
                    var length = math.length(off);
                    var dir = length > 1e-3f ? off / length : math.normalizesafe(s.Position.xz - round.Position.xz, new float2(1f, 0f));
                    s.Target = round.Position.xz + dir * (Berth(in round, pad) + 0.05f);
                }
                if (math.distancesq(to, s.Position.xz) < 1e-8f || !Fires.Near(to, pad, M.FireKeep, Night, out _))
                    return to;
                var step = math.distance(to, s.Position.xz);
                for (int k = 1; k <= 7; k++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var angle = s.Yaw + side * k * 0.45f;
                        var slide = s.Position.xz + new float2(math.sin(angle), math.cos(angle)) * step;
                        if (!Fires.Near(slide, pad, M.FireKeep, Night, out _))
                        {
                            s.Yaw = WrapAngle(s.Yaw + WrapAngle(angle - s.Yaw) * 0.3f);
                            return slide;
                        }
                    }
                s.Speed = 0f;
                return s.Position.xz;
            }

            /// <summary>How far from a fire's middle this creature keeps, padded (FireSpots.Near's reach).</summary>
            float Berth(in FireSpot f, float pad) =>
                f.Radius + math.min(pad + M.FireKeep * FireSpots.Lit(in f, Night), FireSpots.ListedPad);

            /// <summary>
            /// A-121 row 2: the berth it keeps past a lit fire's flames (m). Its species' shyness, more or less by its own
            /// personality, times the fire's heat reach; and, with a fire near, at least the distance where that fire's
            /// heat on top of the weather's would make it too hot (hot-climate and big furred beasts keep back further).
            /// A fire-lover keeps none.
            /// </summary>
            float FireKeepFor(in CreatureState s, ref CreatureBlob b, in ComfortWeather w, bool underground, float heat)
            {
                if (b.Comfort.LovesFire)
                    return 0f;
                var shy = math.saturate(b.Comfort.FireShy * (1f + 0.3f * CreatureBlob.Trait(s.Individual, 7103)));
                var keep = shy * FireSpots.HeatReach(math.max(heat, 1f));
                if (heat > 0f)
                {
                    var ambient = Comfort.FeltHeat(in w, Night, underground);
                    var line = math.max(Comfort.HotLine(in b.Comfort, s.Individual), ambient + 0.08f);
                    var d = 0f;
                    while (d < FireSpots.ListedPad && ambient + FireSpots.Felt(heat, d) > line)
                        d += 0.5f;
                    keep = math.max(keep, d);
                }
                return keep;
            }

            /// <summary>
            /// A-121 row 2, at each comfort check: one about its own business inside its berth of a lit fire (it lit
            /// while it stood there, or the heat got too much) walks back out of it; a fire-lover out of a fire's warmth
            /// now and then goes back to sit by it. True if it set off.
            /// </summary>
            bool FireStep(ref CreatureState s, ref CreatureBlob b, float fireHeat)
            {
                if (!Fires.On || !Free(s.Mode) || Flying(in s) || b.Habitat != Habitat.Land)
                    return false;
                var pad = BodyRadius(s, ref b) + FireClearance;
                if (M.FireKeep > 0f && Fires.Near(s.Position.xz, pad, M.FireKeep, Night, out var here))
                {
                    var away = math.normalizesafe(s.Position.xz - here.Position.xz, new float2(math.sin(s.Yaw), math.cos(s.Yaw)));
                    var rng = new Random(math.hash(new uint2(s.Individual, (uint)(Now * 10f) ^ 0xF1E5u)) | 1u);
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = here.Position.xz + away * (Berth(in here, pad) + rng.NextFloat(0.5f, 1.5f));
                    s.Timer = rng.NextFloat(6f, 12f);
                    System.Threading.Interlocked.Increment(ref Shelters.Data.FireBacked);
                    return true;
                }
                if (b.Comfort.LovesFire && fireHeat < 0.1f && Fires.Nearest(s.Position.xz, 30f, Night, out var fire))
                {
                    var rng = new Random(math.hash(new uint2(s.Individual, (uint)(Now * 10f) ^ 0x10F3u)) | 1u);
                    if (rng.NextFloat() > 0.3f)
                        return false;
                    var dir = math.normalizesafe(s.Position.xz - fire.Position.xz, new float2(0f, 1f));
                    SetMode(ref s, CreatureMode.Shelter);
                    s.Target = fire.Position.xz + dir * (fire.Radius + pad + rng.NextFloat(0.3f, 1f));
                    s.Focus = fire.Position.xz;
                    s.TargetHeight = (float)ShelterNeed.Fire;
                    s.Timer = rng.NextFloat(40f, 120f);
                    System.Threading.Interlocked.Increment(ref Shelters.Data.FireDrawn);
                    return true;
                }
                return false;
            }
        }
    }
}
