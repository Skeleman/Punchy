using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: petting, being held, greeting, cave life, roosts, flight. Split from CreatureBrainSystem.cs by area (#61);
        // the system, Execute and Think live there.
        partial struct BrainJob
        {
            /// <summary>
            /// Being petted: turned to the person, it leans into the hand with its tail going (stage 0) or stands for it,
            /// watching them (1). When the person leaves off (or walks away) it goes on with its day; a friendly one
            /// keeps them company.
            /// </summary>
            void Petted(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float sit, ref float bow, ref float desiredLook)
            {
                s.Timer -= dt;
                var hasPerson = FindHolder(s.Holder, out var person, out _);
                if (s.Timer <= 0f || !hasPerson || math.distance(person.xz, s.Position.xz) > Interactions.Reach + 1.5f)
                {
                    s.Holder = 0;
                    if (b.Friendly && hasPerson)
                    {
                        SetMode(ref s, CreatureMode.Greet);
                        s.Timer = rng.NextFloat(20f, 45f);
                    }
                    else
                    {
                        Pause(ref s, ref b, ref rng);
                        // Tidying itself after being handled.
                        if ((b.Idles & IdleActions.Groom) != 0 && rng.NextFloat() < 0.5f)
                        {
                            s.Action = IdleAction.Groom;
                            s.ActionLength = rng.NextFloat(2f, 4f);
                        }
                    }
                    return;
                }
                s.Focus = person.xz;
                TurnTowards(ref s, ref b, person.xz, dt);
                var to = person.xz - s.Position.xz;
                var lookAt = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                if (s.Stage == 0)
                {
                    // Leaning in: head pressed up to the hand, chest down a little, tail going.
                    desiredLook = lookAt * 0.6f;
                    bow = 0.3f;
                    sit = b.Friendly ? 0.6f : 0f;
                    if (s.Action == IdleAction.None && (b.Idles & IdleActions.TailFlick) != 0 && rng.NextFloat() < dt * 1.5f)
                    {
                        s.Action = IdleAction.TailFlick;
                        s.ActionTime = 0f;
                        s.ActionLength = rng.NextFloat(1.5f, 2.5f);
                    }
                }
                else
                    desiredLook = lookAt;   // putting up with it, keeping an eye on them
            }

            /// <summary>
            /// In someone's arms, across their chest. One that isn't friendly puts up with it for a while, then wriggles
            /// (a shake), and before long squirms free and runs. If the holder has gone, it's back on the ground.
            /// </summary>
            void Held(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                s.Speed = 0f;
                if (!FindHolder(s.Holder, out var holder, out var yaw, out var holderSpecies))
                {
                    Interactions.PutDown(ref s, Terrain, s.Position, ref rng);
                    return;
                }
                s.Position = Interactions.Hands(Carry, holderSpecies, holder, yaw, Interactions.HalfWidth(ref b, s.Scale));
                s.Yaw = yaw + math.PI * 0.5f;
                s.YawRate = 0f;
                s.Timer -= dt;
                if (s.Timer < 2f && s.Action == IdleAction.None && (b.Idles & IdleActions.Shake) != 0)
                {
                    s.Action = IdleAction.Shake;
                    s.ActionTime = 0f;
                    s.ActionLength = 1.2f;
                }
                if (s.Timer <= 0f)
                {
                    // Free: it drops to the ground beside them and runs.
                    var side = new float3(math.cos(yaw), 0f, -math.sin(yaw)) * (rng.NextBool() ? 0.6f : -0.6f);
                    Interactions.PutDown(ref s, Terrain, holder + side, ref rng);
                    Interactions.Bolt(ref s, ref b, Terrain, holder, ref rng);
                    s.Target = s.Position.xz + math.normalizesafe(s.Position.xz - holder.xz, new float2(1f, 0f)) * 10f;
                }
            }

            /// <summary>
            /// Someone near is emoting. Folk stop, turn to them and answer in kind (a wave back, a bow, a cheer; some
            /// dance along, the rest cheer them on), each after a moment of their own, and a few shy ones not at all.
            /// Friendly animals come over to see; one sitting by them play-bows back to a bow. A curious one comes partway
            /// and watches (Greet's Curious stage); the skittish run sooner (commotion, in Think); some pay no heed.
            /// </summary>
            void Answer(ref CreatureState s, ref CreatureBlob b, ref Random rng, IdleAction emote, float3 player, float distance, float dt,
                        FolkStance stance)
            {
                if (b.Upright)
                {
                    if (distance > 20f || !(Free(s.Mode) || s.Mode == CreatureMode.Work))
                        return;
                    // (Wary of the player's folk: no answer. Annoyed: it turns its back and walks off a few metres.)
                    if (stance == FolkStance.Wary)
                        return;
                    if (stance == FolkStance.Annoyed)
                    {
                        if (s.Mode != CreatureMode.Wander && s.Mode != CreatureMode.Work && distance < 12f)
                        {
                            SetMode(ref s, CreatureMode.Wander);
                            s.Target = s.Position.xz + math.normalizesafe(s.Position.xz - player.xz, rng.NextFloat2Direction()) * rng.NextFloat(5f, 8f);
                        }
                        return;
                    }
                    // Answering already: keep facing them.
                    if (s.Action >= IdleAction.Wave)
                    {
                        TurnTowards(ref s, ref b, player.xz, dt);
                        return;
                    }
                    // (Kin and the warm are never too shy to answer.)
                    var shy = CreatureBlob.Trait(s.Individual, 21) < -0.75f && stance != FolkStance.Kin && stance != FolkStance.Warm;
                    if (s.Action != IdleAction.None || shy || rng.NextFloat() > dt * 2.5f)
                        return;
                    var reply = emote != IdleAction.Dance ? emote : CreatureBlob.Trait(s.Individual, 22) > 0f ? IdleAction.Dance : IdleAction.Cheer;
                    // A rival answers anything with a mock bow (a dwarf to an elf, or the other way).
                    if (stance == FolkStance.Rival)
                        reply = IdleAction.Bow;
                    SetMode(ref s, CreatureMode.Idle);
                    s.Action = reply;
                    s.ActionTime = 0f;
                    s.ActionLength = reply == IdleAction.Dance ? rng.NextFloat(4f, 7f) : reply == IdleAction.Wave ? 2f : 1.8f;
                    s.Timer = s.ActionLength + rng.NextFloat(0.5f, 1.5f);
                    s.Speed = 0f;
                    s.Focus = player.xz;
                    return;
                }
                if (b.Reaction == EmoteReaction.Curious)
                {
                    if (distance < 25f && Free(s.Mode))
                    {
                        SetMode(ref s, CreatureMode.Greet);
                        s.Stage = Curious;
                        s.Timer = rng.NextFloat(6f, 12f);
                    }
                    return;
                }
                if (b.Reaction != EmoteReaction.Wags)
                    return;
                if (emote != IdleAction.Bow && distance < 20f && Free(s.Mode))
                {
                    SetMode(ref s, CreatureMode.Greet);
                    s.Timer = rng.NextFloat(15f, 30f);
                }
                else if (emote == IdleAction.Bow && s.Mode == CreatureMode.Greet && distance < 4f && s.Action == IdleAction.None &&
                         (b.Idles & IdleActions.Stretch) != 0)
                {
                    s.Action = IdleAction.Stretch;
                    s.ActionTime = 0f;
                    s.ActionLength = 2.2f;
                }
            }

            /// <summary>
            /// The folk's regard for the player's body, unasked (Z-52): a wary one standing within 3 m steps back to keep
            /// 4-5 m off; kin, the warm and rivals standing about now and then wave (a rival bows, mocking) at someone
            /// 1.5-6 m off in front of them.
            /// </summary>
            void Regarding(ref CreatureState s, ref CreatureBlob b, ref Random rng, FolkStance stance, float3 player, float distance, float dt)
            {
                if (s.Mode != CreatureMode.Idle || s.Action != IdleAction.None || s.Stage >= MessageWait)
                    return;
                if (stance == FolkStance.Wary)
                {
                    if (distance < 3f)
                    {
                        SetMode(ref s, CreatureMode.Wander);
                        s.Target = s.Position.xz + math.normalizesafe(s.Position.xz - player.xz, rng.NextFloat2Direction()) * (4.5f - distance);
                    }
                    return;
                }
                if (stance == FolkStance.Annoyed || distance < 1.5f || distance > 6f || rng.NextFloat() > dt * 0.05f)
                    return;
                var to = player.xz - s.Position.xz;
                if (math.abs(WrapAngle(math.atan2(to.x, to.y) - s.Yaw)) > 1f)
                    return;
                s.Action = stance == FolkStance.Rival ? IdleAction.Bow : IdleAction.Wave;
                s.ActionTime = 0f;
                s.ActionLength = s.Action == IdleAction.Wave ? 2f : 1.8f;
                s.Focus = player.xz;
            }

            /// <summary>Greet's stage for a curious animal come to look (EmoteReaction.Curious), not to sit by them.</summary>
            const byte Curious = 1;

            /// <summary>
            /// Trots over to the person (to their side, not into them), then sits by them looking up and wagging its
            /// tail; follows when they walk off (as far as its home range allows), until it loses interest.
            /// </summary>
            void Greet(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float3 player, float walk, float run,
                       ref float targetSpeed, ref bool moving, ref float sit, ref float desiredLook)
            {
                s.Timer -= dt;
                var to = player.xz - s.Position.xz;
                var distance = math.length(to);
                if (s.Timer <= 0f || distance > 40f)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                // Its own spot round the person (one of five, by individual), so several don't pile into each other.
                var dir = math.normalizesafe(to, new float2(0f, 1f));
                var angle = ((int)(s.Individual % 5u) - 2) * 0.75f;
                var away = new float2(dir.x * math.cos(angle) - dir.y * math.sin(angle), dir.x * math.sin(angle) + dir.y * math.cos(angle));
                var spot = player.xz - away * (1.3f + b.BodyLength * s.Scale * 0.5f);
                var lookAt = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                if (s.Stage == Curious)
                {
                    // Curious: up to a wary few metres (well outside where it would bolt), slowly, then stands watching
                    // them, sniffing now and then, until it's seen enough.
                    var standoff = math.max(b.FleeDistance * s.Scale * 0.6f, 2f + b.BodyLength * s.Scale);
                    if (distance > standoff + 0.5f)
                    {
                        s.Target = player.xz - dir * standoff;
                        targetSpeed = walk * 0.6f;
                        moving = true;
                        desiredLook = lookAt;
                    }
                    else
                    {
                        TurnTowards(ref s, ref b, player.xz, dt);
                        desiredLook = lookAt * 0.6f;
                        if (s.Action == IdleAction.None && (b.Idles & IdleActions.Sniff) != 0 && rng.NextFloat() < dt * 0.4f)
                        {
                            s.Action = IdleAction.Sniff;
                            s.ActionTime = 0f;
                            s.ActionLength = rng.NextFloat(1f, 2f);
                        }
                    }
                    return;
                }
                if (math.distance(s.Position.xz, spot) > 0.6f)
                {
                    s.Target = spot;
                    targetSpeed = distance > 6f ? run * 0.7f : walk;
                    moving = true;
                    desiredLook = lookAt;
                }
                else
                {
                    // Sitting by them, facing them, tail going.
                    TurnTowards(ref s, ref b, player.xz, dt);
                    sit = 1f;
                    desiredLook = lookAt * 0.5f;
                    if (s.Action == IdleAction.None && rng.NextFloat() < dt * 0.8f)
                    {
                        s.Action = IdleAction.TailFlick;
                        s.ActionTime = 0f;
                        s.ActionLength = rng.NextFloat(1.5f, 3f);
                    }
                }
            }

            // ------------------------------------------------------------------------------- caves and roosts

            /// <summary>
            /// A drinking spot by underground water: one of the Pool anchors the caves export (on a pool's rim or a
            /// stream bank, facing the water), near enough on about the same level, spread over the anchor's size.
            /// </summary>
            bool FindCaveWater(ref CreatureState s, ref CreatureBlob b, ref Random rng, out float2 shore, out float2 water)
            {
                shore = water = default;
                if (!Terrain.Anchors.IsCreated ||
                    !Anchors.Pick(ref Terrain.Anchors.Value, s.Position, 20f, AnchorKind.Pool, ref rng, out var pool) ||
                    math.abs(pool.Position.y - s.Position.y) > 3f)
                    return false;
                var spot = pool.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(0f, pool.Size * 0.5f);
                if (!CaveFloor(spot, pool.Position.y))
                    return false;
                shore = spot - pool.Facing * (b.BodyLength * 0.4f * s.Scale);
                water = spot + pool.Facing;
                return true;
            }

            /// <summary>
            /// Where a cave dweller runs to next: a point along the passages (a cave node within 15 m, on about its level),
            /// the one that puts most distance between it and the threat without heading towards it; straight away
            /// if there's none (the wall-sliding in Advance keeps it off the rock either way).
            /// </summary>
            float2 CaveEscape(in CreatureState s, float2 threat, ref Random rng)
            {
                var away = math.normalizesafe(s.Position.xz - threat, rng.NextFloat2Direction());
                var best = s.Position.xz + away * 12f;
                if (!Terrain.Caves.IsCreated)
                    return best;
                ref var set = ref Terrain.Caves.Value;
                var bestScore = float.MinValue;
                for (int n = 0; n < set.Nodes.Length; n++)
                {
                    var p = set.Nodes[n].Position;
                    var to = p.xz - s.Position.xz;
                    var d = math.length(to);
                    // Near ones: the next stretch of passage (it picks another on arriving).
                    if (d < 2.5f || d > 15f || math.abs(p.y - s.Position.y) > 4f)
                        continue;
                    // Not past the threat: the way to it mustn't lead towards what it's running from.
                    if (math.dot(to / d, away) < -0.2f)
                        continue;
                    // (A little chance in it, so one stuck at a wall tries another way.)
                    var score = math.distance(p.xz, threat) - d * 0.3f + rng.NextFloat(0f, 4f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = p.xz;
                    }
                }
                return best;
            }

            /// <summary>
            /// Whether a cave dweller at this height can step here: cave floor at about the same level, under the rock
            /// (not out of the mouth into the open: cave dwellers stay in their cave).
            /// </summary>
            bool CaveFloor(float2 xz, float y) => CaveFloorAt(xz, y, out _);

            /// <summary>
            /// The cave floor under a point at about body height above <paramref name="y"/>, if there's cave air there
            /// (so not in the rock: inside solid rock FloorUnder answers "in a cave" with the floor where the point
            /// is) and the floor is near that level, under the rock (not out of the mouth).
            /// </summary>
            bool CaveFloorAt(float2 xz, float y, out float floor)
            {
                floor = y;
                var above = new float3(xz.x, y + 0.6f, xz.y);
                if (Terrain.CaveDistance(above) >= 0f)
                    return false;
                floor = Terrain.FloorUnder(above, out var inCave);
                // (Up a step of up to half a metre; down off a ledge of up to 3 m. A real floor is below the point
                // asked from: FloorUnder answers with the point's own height when its search starts in rock.)
                return inCave && floor < above.y - 0.05f && floor - y < 0.5f && y - floor < 3f && floor < Terrain.SampleHeight(xz) - 1f;
            }

            /// <summary>A short, low loop to somewhere nearby: a bat hawking for insects.</summary>
            void NightLoop(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                s.Target = s.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(6f, 20f);
                s.TargetHeight = Terrain.SampleHeight(s.Target) + math.lerp(b.FlightHeight.x, b.FlightHeight.y, rng.NextFloat());
            }

            /// <summary>
            /// Picks a roost: the ceiling of the nearest cave's inner passage (within 150 m), else the underside of a
            /// branch nearby.
            /// </summary>
            /// <summary>How many are roosting at (or on their way to) a roost: their Anchor is on it.</summary>
            int RoostUsers(in Anchor roost)
            {
                var users = 0;
                var r2 = (roost.Size + 0.5f) * (roost.Size + 0.5f);
                for (int i = 0; i < States.Length; i++)
                    if (States[i].Mode == CreatureMode.Roost && math.distancesq(States[i].Anchor, roost.Position) < r2)
                        users++;
                return users;
            }

            void StartRoost(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Roost);
                s.Timer = rng.NextFloat(60f, 200f);
                s.LairCave = -1;
                // A Roost anchor the caves export: a spot on a chamber ceiling, which bats share (spread over its Size), up
                // to its Capacity: a full one, it tries another.
                var roost = default(Anchor);
                var picked = false;
                for (int tries = 0; tries < 4 && !picked && Terrain.Anchors.IsCreated; tries++)
                    picked = Anchors.Pick(ref Terrain.Anchors.Value, s.Position, 150f, AnchorKind.Roost, ref rng, out roost) &&
                             (roost.Capacity == 0 || RoostUsers(roost) < roost.Capacity);
                if (picked && Terrain.Caves.IsCreated && roost.InCave && roost.OwnerIndex < Terrain.Caves.Value.Caves.Length)
                {
                    ref var caves = ref Terrain.Caves.Value;
                    ref var home = ref caves.Caves[roost.OwnerIndex];
                    // The passage node nearest the roost: the way there.
                    var nearest = 0;
                    for (int n = 1; n < home.NodeCount; n++)
                        if (math.distancesq(caves.Nodes[home.FirstNode + n].Position, roost.Position) <
                            math.distancesq(caves.Nodes[home.FirstNode + nearest].Position, roost.Position))
                            nearest = n;
                    s.LairCave = (short)roost.OwnerIndex;
                    s.LairNode = (short)nearest;
                    var spread = rng.NextFloat2Direction() * rng.NextFloat(0f, roost.Size * 0.8f);
                    s.Anchor = roost.Position + new float3(spread.x, 0f, spread.y);
                    s.AnchorSize = 0.3f;
                    s.Stage = 1;
                    return;
                }
                if (Terrain.Caves.IsCreated)
                {
                    ref var set = ref Terrain.Caves.Value;
                    var best = 150f;
                    for (int i = 0; i < set.Caves.Length; i++)
                    {
                        var d = math.distance(set.Caves[i].Mouth.xz, s.Position.xz);
                        if (d < best && set.Caves[i].NodeCount >= 2 && set.Caves[i].NodeCount < 80)
                        {
                            best = d;
                            s.LairCave = (short)i;
                        }
                    }
                }
                if (s.LairCave >= 0)
                {
                    ref var set = ref Terrain.Caves.Value;
                    ref var cave = ref set.Caves[s.LairCave];
                    // Somewhere in the far half of the cave, on the ceiling straight above the passage's middle.
                    s.LairNode = (short)rng.NextInt(cave.NodeCount / 2, cave.NodeCount);
                    var p = set.Nodes[cave.FirstNode + s.LairNode].Position;
                    for (int k = 0; k < 60 && Terrain.CaveDistance(p + new float3(0f, 0.2f, 0f)) < 0f; k++)
                        p.y += 0.2f;
                    s.Anchor = p + rng.NextFloat3Direction() * new float3(0.3f, 0f, 0.3f);
                    s.AnchorSize = 0.3f;
                    s.Stage = 1;
                    return;
                }
                if (HasFlora && FloraPlacement.FindTree(ref Flora.Value, Terrain, s.Position.xz, 60f, 4f, ref rng, out var tree))
                {
                    var under = tree.Base.xz + rng.NextFloat2Direction() * (tree.TrunkRadius + tree.CrownRadius * 0.3f);
                    s.Anchor = new float3(under.x, tree.Base.y + tree.CrownBase, under.y);
                    s.AnchorSize = 0.2f;
                    s.Stage = 198;
                    return;
                }
                // Nowhere to hang: keep flying.
                NightLoop(ref s, ref b, ref rng);
            }

            /// <summary>
            /// A bat's jinking: its heading swings off the line to its target and back, a couple of times a second
            /// (after insects), each bat on its own beat. None near the target, so it still lands and hangs up.
            /// </summary>
            static float Swerve(in CreatureState s, ref CreatureBlob b, float distance)
            {
                if (!b.Roosts || s.Fly < 0.5f)
                    return 0f;
                var own = (s.Individual % 89) * 0.37f;
                return (0.7f * math.sin(s.Clock * 2.3f + own) + 0.45f * math.sin(s.Clock * 5.3f + own * 1.7f)) * math.saturate((distance - 2f) / 4f);
            }

            /// <summary>Flies straight at a point (no ground to keep clear of: inside a cave). Returns the distance left.</summary>
            float FlyDirect(ref CreatureState s, ref CreatureBlob b, float3 target, float speed, float dt, ref float vertical)
            {
                var to = target - s.Position;
                var distance = math.length(to);
                var wanted = WrapAngle(math.atan2(to.x, to.z) - s.Yaw) + Swerve(s, ref b, distance);
                var turn = math.clamp(wanted, -b.TurnRate * 2f * dt, b.TurnRate * 2f * dt);
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = turn / math.max(dt, 1e-4f);
                var flare = math.saturate(1f - distance / CreatureMotion.FlareDistance);
                var wantSpeed = math.lerp(math.max(speed, b.FlySpeed * CreatureMotion.MinAirspeed), math.max(distance * 1.5f, 0.3f), flare);
                s.Speed += (wantSpeed - s.Speed) * math.min(1f, dt * 3f);
                // Along its heading, rising or sinking no steeper than the flight angle (the last moments of a landing
                // excepted): it swings round to line up rather than lifting straight up.
                var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
                var flat = math.length(to.xz);
                var level = math.abs(to.y) < 0.3f;
                var across = level ? math.min(s.Speed, flat / math.max(dt, 1e-4f)) : s.Speed;
                var steepest = across * CreatureMotion.MaxClimb;
                var vy = math.clamp(to.y * 2f, -steepest, steepest);
                var move = forward * across + new float3(0f, vy, 0f);
                s.Position += move * dt;
                vertical = math.max(math.clamp(vy / math.max(b.FlySpeed, 1f), -0.6f, 0.5f), flare * 0.45f);
                return math.length(target - s.Position);
            }

            void Roost(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float vertical)
            {
                var speed = b.FlySpeed * 0.6f;
                var hangAt = s.Anchor - new float3(0f, (b.BodyLength * 0.5f + b.FrontRadius) * s.Scale + 0.05f, 0f);
                var waking = SleepTime(ref b) < 0.3f;
                if (s.Stage == 200)
                {
                    // Hanging head down, asleep, till dusk.
                    vertical = -1f;
                    s.Speed = 0f;
                    s.Position = hangAt;
                    s.Timer -= dt;
                    if (waking && s.Timer <= 0f)
                    {
                        if (s.LairCave >= 0)
                        {
                            s.Stage = (byte)(100 + math.min(s.LairNode, 89));
                            s.StageTime = 0f;
                        }
                        else
                            NightLoop(ref s, ref b, ref rng);
                    }
                    return;
                }
                if (s.Stage == 198)
                {
                    // To a branch outdoors: flying as usual (clear of the ground), to hang just under it.
                    if (FlyTo(ref s, ref b, hangAt.xz, hangAt.y, b.FlySpeed, dt, ref vertical))
                    {
                        s.Stage = 200;
                        s.StageTime = 0f;
                    }
                    else if (s.StageTime > 60f)
                        NightLoop(ref s, ref b, ref rng);
                    return;
                }
                if (s.Stage == 199)
                {
                    if (FlyDirect(ref s, ref b, hangAt, speed * 0.6f, dt, ref vertical) < 0.15f)
                    {
                        s.Stage = 200;
                        s.StageTime = 0f;
                    }
                    else if (s.StageTime > 30f)
                        NightLoop(ref s, ref b, ref rng);
                    return;
                }
                if (s.LairCave < 0 || !Terrain.Caves.IsCreated)
                {
                    NightLoop(ref s, ref b, ref rng);
                    return;
                }
                ref var set = ref Terrain.Caves.Value;
                ref var cave = ref set.Caves[s.LairCave];
                if (s.Stage == 1)
                {
                    // To the cave's mouth, from outside.
                    var mouth = cave.Mouth;
                    if (FlyTo(ref s, ref b, mouth.xz, mouth.y, b.FlySpeed, dt, ref vertical) || math.distance(s.Position, mouth) < 1.2f)
                    {
                        s.Stage = 10;
                        s.StageTime = 0f;
                    }
                    else if (s.StageTime > 60f)
                        NightLoop(ref s, ref b, ref rng);
                    return;
                }
                if (s.Stage >= 10 && s.Stage < 100)
                {
                    // In along the passage, node by node, then up to the ceiling.
                    var node = s.Stage - 10;
                    if (FlyDirect(ref s, ref b, set.Nodes[cave.FirstNode + node].Position, speed, dt, ref vertical) < 1f)
                    {
                        s.Stage = (byte)(node >= s.LairNode ? 199 : s.Stage + 1);
                        s.StageTime = 0f;
                    }
                    return;
                }
                // Out again at dusk, back along the passage to the mouth, then off hawking.
                var back = s.Stage - 100;
                var target = back >= 0 ? set.Nodes[cave.FirstNode + back].Position : cave.Mouth;
                if (FlyDirect(ref s, ref b, target, speed, dt, ref vertical) < 1f)
                {
                    if (back <= 0)
                        NightLoop(ref s, ref b, ref rng);
                    else
                    {
                        s.Stage--;
                        s.StageTime = 0f;
                    }
                }
            }

            // ------------------------------------------------------------------------------- flight

            /// <summary>Whether a flying species is in the air now (flying somewhere, fleeing or hunting).</summary>
            static bool Flying(in CreatureState s) =>
                (s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Hunt) && s.Stage >= 1 || s.Mode == CreatureMode.Flee ||
                s.Mode == CreatureMode.Work && s.Task == CreatureTask.Messenger && s.Stage != 1 ||
                s.Mode == CreatureMode.Roost && s.Stage != 200;

            /// <summary>
            /// Takes off for somewhere new: a branch in a tree nearby (more likely by its Perching, and always to
            /// sleep), or a patch of ground.
            /// </summary>
            bool StartFlight(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool toSleep)
            {
                var range = math.max(b.WanderRadius, 20f);
                // (#12/A-12: somewhere ahead of it, within 90 deg of its heading, so the turn to face the new spot stays
                // small; a spot behind meant a half turn in a couple of seconds, songbirds' wandering at 25-31 deg/s.)
                // (#12/A-15: ground spots within 60 deg, so with the drift of a takeoff hop the bearing stays under 90.)
                var ahead = s.Yaw + rng.NextFloat(-math.PI / 3f, math.PI / 3f);
                var aheadDir = new float2(math.sin(ahead), math.cos(ahead));
                // A soarer mostly goes up to circle a while over somewhere near (not straight after a spell of it).
                if (b.Soars && !toSleep && s.Stage != 2 && rng.NextFloat() < 0.75f)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 2;
                    s.Focus = s.Position.xz + rng.NextFloat2Direction() * range * rng.NextFloat(0.2f, 0.8f);
                    s.TargetHeight = Cruise(s, ref b, s.Focus);
                    s.Timer = rng.NextFloat(60f, 240f);
                    s.Target = s.Focus;
                    return true;
                }
                if (toSleep && b.Nests != NestSite.None && b.Nests != NestSite.Ground && s.AnchorSize > 0f)
                {
                    // Home to its nest.
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 1;
                    s.Target = s.Anchor.xz;
                    s.TargetHeight = s.Anchor.y;
                    return true;
                }
                if (HasFlora && (toSleep || rng.NextFloat() < b.Perching) &&
                    FloraPlacement.FindTree(ref Flora.Value, Terrain, s.Position.xz + aheadDir * range * 0.5f, range * 0.6f, 3f, ref rng, out var tree) &&
                    (toSleep || AheadAndClear(s, tree.Base.xz)))
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 1;
                    var up = tree.CrownBase + (tree.Height - tree.CrownBase) * rng.NextFloat(0.25f, 0.75f);
                    s.Target = tree.Base.xz + rng.NextFloat2Direction() * (tree.TrunkRadius + tree.CrownRadius * rng.NextFloat(0.25f, 0.7f));
                    s.TargetHeight = tree.Base.y + up;
                    return true;
                }
                if (toSleep)
                    return false;
                var spot = s.Position.xz + aheadDir * range * rng.NextFloat(0.5f, 1.2f);
                if (!OpenGround(spot))
                    return false;
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                s.Target = spot;
                s.TargetHeight = Terrain.SampleHeight(spot);
                return true;
            }

            /// <summary>
            /// A tree worth flying to: within 75 deg of its heading and at least 8 m off. (#12/A-14: the tree search
            /// circle reaches behind the bird and right next to it; the per-flight trace showed those picks, at bearings
            /// of 100-170 deg or 4-10 m away, made the long turns and the circling round the spot.)
            /// </summary>
            static bool AheadAndClear(in CreatureState s, float2 spot)
            {
                var to = spot - s.Position.xz;
                var distance = math.length(to);
                return distance >= 8f && math.abs(WrapAngle(math.atan2(to.x, to.y) - s.Yaw)) <= math.radians(75f);
            }

            /// <summary>
            /// Soaring: round and round over its centre (Focus) at its own radius and turning its own way, steering for a
            /// point a little ahead on the circle so it never slows as if to land; when its time is up, off to come down
            /// somewhere.
            /// </summary>
            void Soar(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                s.Timer -= dt;
                if (s.Timer <= 0f && b.Breaches)
                {
                    // Back down into the deep water under the circle (where it came up), nose first.
                    s.Stage = 3;
                    s.Target = s.Focus;
                    var water = Terrain.SampleGround(s.Focus);
                    s.TargetHeight = math.max(water.WaterSurface - b.SwimDepth * s.Scale * 0.5f, water.Height + 0.5f);
                    return;
                }
                if (s.Timer <= 0f)
                {
                    if (StartFlight(ref s, ref b, ref rng, false))
                        return;
                    s.Timer = 10f;   // (nowhere to come down just here: round again)
                }
                // A sky serpent now and then climbs to a higher pass, or sinks to a lower one, over the same water.
                if (b.Breaches && rng.NextFloat() < dt / 40f)
                    s.TargetHeight = Cruise(s, ref b, s.Focus) + rng.NextFloat(0f, 45f);
                var radius = math.lerp(30f, 70f, CreatureBlob.Trait(s.Individual, 21) * 0.5f + 0.5f);
                var way = CreatureBlob.Trait(s.Individual, 22) > 0f ? 1f : -1f;
                var from = s.Position.xz - s.Focus;
                var angle = math.atan2(from.y, from.x) + 0.6f * way;
                s.Target = s.Focus + new float2(math.cos(angle), math.sin(angle)) * radius;
                // (Its height over the circle: its cruising height over the middle, held as the ground falls away.)
                s.TargetHeight = math.max(s.TargetHeight, Cruise(s, ref b, s.Position.xz));
            }

            /// <summary>Touches down: on a branch it settles to rest a while, on the ground it looks about.</summary>
            void Land(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                s.Speed = 0f;
                var onBranch = s.Position.y > Terrain.SampleHeight(s.Position.xz) + 0.3f;
                if (onBranch && rng.NextFloat() < 0.5f)
                {
                    SetMode(ref s, CreatureMode.Rest);
                    s.Timer = rng.NextFloat(10f, 40f);
                }
                else
                    Pause(ref s, ref b, ref rng);
            }

            /// <summary>Cruising height over a point: its own share of the species' range above the ground or water.</summary>
            float Cruise(in CreatureState s, ref CreatureBlob b, float2 xz)
            {
                var g = Terrain.SampleGround(xz);
                var floor = g.HasWater ? math.max(g.Height, g.WaterSurface) : g.Height;
                return floor + math.lerp(b.FlightHeight.x, b.FlightHeight.y, CreatureBlob.Trait(s.Individual, 11) * 0.5f + 0.5f);
            }

            /// <summary>
            /// Flies towards a point and height: climbs to cruising height, glides down to it as it comes near, and
            /// keeps clear of the ground. Returns true once it's there. Pitches the body with the climb or descent.
            /// </summary>
            bool FlyTo(ref CreatureState s, ref CreatureBlob b, float2 target, float targetHeight, float speed, float dt, ref float vertical)
            {
                var to = target - s.Position.xz;
                var distance = math.length(to);
                var flare = math.saturate(1f - distance / CreatureMotion.FlareDistance);
                // In the flare it no longer wheels round to face the spot: it eases its turning off and drifts the last
                // stretch onto the spot, a little sideways if need be (#12: at the full turn rate it circled the spot
                // tightly, waiting for its height to settle, and songbirds' wandering turned at over 100 deg/s).
                var settle = flare > 0f && FlareSettleOff.Data == 0;
                // (Right over the spot the bearing means nothing: hold the heading.)
                var wanted = distance < 0.1f ? 0f : WrapAngle(math.atan2(to.x, to.y) - s.Yaw) + Swerve(s, ref b, distance);
                var turnRate = b.TurnRate * 1.5f * (settle ? 1f - flare : 1f);
                // (#12/A-13: the takeoff turn damped: while it is still getting off the ground the turn rate builds up with its flight.)
                turnRate *= math.lerp(0.3f, 1f, math.saturate(s.Fly));
                var turn = math.clamp(wanted, -turnRate * dt, turnRate * dt);
                // (#12/A-13: and the turn rate itself changes smoothly, at most 100 deg/s each second, so a new spot behind
                // it eases into the turn rather than snapping to the full rate.)
                var rateStep = math.radians(100f) * dt * dt;
                turn = math.clamp(turn, s.YawRate * dt - rateStep, s.YawRate * dt + rateStep);
                if (turn * wanted > 0f && math.abs(turn) > math.abs(wanted))
                    turn = wanted;
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = turn / math.max(dt, 1e-4f);
                // Airspeed never drops below the minimum (a bird above its landing spot circles down rather than
                // sinking), except in the flare, where it slows to touch down within a body length or two.
                var minSpeed = b.FlySpeed * CreatureMotion.MinAirspeed;
                var cruiseSpeed = math.max(speed * math.saturate(math.cos(wanted) + 0.4f), minSpeed);
                var wantSpeed = math.lerp(cruiseSpeed, math.max(distance * 1.5f, 0.3f), flare);
                // Taking off from standing: a hop forward, straight into the minimum speed.
                if (s.Fly < 0.5f)
                    s.Speed = math.max(s.Speed, minSpeed);
                s.Speed += (wantSpeed - s.Speed) * math.min(1f, dt * 2f);
                if (flare <= 0f)
                    s.Speed = math.max(s.Speed, minSpeed);
                // Always moving forward: arriving over the spot too high, it carries on past and circles down.
                var step = s.Speed * dt;
                var heading = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                if (settle && distance > 1e-3f)
                    heading = math.normalizesafe(math.lerp(heading, to / distance, flare), heading);
                s.Position.xz += heading * math.min(step, settle ? distance : step);

                // Height: cruising, or coming down the glide slope to the landing spot; never climbing or sinking
                // steeper than the flight angle allows for how far it moved forward.
                // (Never below where it's landing: a nest or branch can be higher than its usual cruising height.)
                var cruise = math.max(math.max(Cruise(s, ref b, s.Position.xz), Cruise(s, ref b, target)), targetHeight);
                // (The slope meets the landing height just short of the spot, so it touches down still moving forward.
                // Landing on the ground: the ground right where it is; on a branch: the branch's height.)
                var groundHere = Terrain.SampleHeight(s.Position.xz);
                var onGround = targetHeight < Terrain.SampleHeight(target) + 0.1f;
                var touchdown = onGround && distance < CreatureMotion.FlareDistance ? groundHere : targetHeight;
                var wantY = math.min(cruise, touchdown + math.max(distance - 0.3f, 0f) * CreatureMotion.GlideSlope);
                // (In the flare, the last few centimetres settle as it slows.)
                var steepest = math.max(step / math.max(dt, 1e-4f) * CreatureMotion.MaxClimb, flare * 0.4f);
                // (#12/A-15: within 6 m of the spot it closes on the glide slope harder and may dive steeper, so it
                // arrives low enough to land rather than overshooting high and wheeling back round at minimum airspeed.)
                var near = math.saturate(1f - distance / 6f);
                var gain = math.lerp(1.5f, 4f, near);
                var vy = math.clamp((wantY - s.Position.y) * gain, -steepest * math.lerp(1f, 1.8f, near), steepest);
                // In the flare the last of the height goes at a steady rate (not ever more slowly), so it settles as
                // it slows rather than hanging over the spot.
                var gap = wantY - s.Position.y;
                if (flare > 0f && math.abs(gap) > 1e-3f)
                    vy = math.sign(gap) * math.min(math.max(math.abs(vy), 0.3f * flare), math.abs(gap) / math.max(dt, 1e-4f));
                s.Position.y += vy * dt;
                // Clear of the ground (except right at the landing spot).
                var floor = Terrain.SampleHeight(s.Position.xz) + (distance > 1f ? 0.4f : 0f);
                s.Position.y = math.max(s.Position.y, floor);
                // Nose with the climb or descent; nose up in the flare.
                vertical = math.max(math.clamp(vy / math.max(b.FlySpeed, 1f), -0.6f, 0.5f), flare * 0.45f);
                // Touches down where it is, once it's down at the landing height near the spot (no snap).
                if (distance < 0.5f && math.abs(s.Position.y - touchdown) < 0.005f)
                {
                    s.Position.y = touchdown;
                    return true;
                }
                return false;
            }

            /// <summary>
            /// A bird of prey after a fish: circles above it (1), folds and dives at it (2), then beats back up and away
            /// (3). The fish decides for itself whether it was caught (see Struck).
            /// </summary>
            void Dive(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float vertical)
            {
                s.Timer -= dt;
                var hasPrey = Lookup.TryGetValue(s.Partner, out var p);
                if (s.Stage <= 1)
                {
                    if (!hasPrey || s.Timer <= 0f)
                    {
                        AfterHunt(ref s, ref b, ref rng);
                        return;
                    }
                    var prey = States[p];
                    var angle = s.Clock * 0.45f + (s.Individual % 10);
                    var circle = prey.Position.xz + new float2(math.cos(angle), math.sin(angle)) * 10f;
                    FlyTo(ref s, ref b, circle, Cruise(s, ref b, prey.Position.xz), b.FlySpeed * 0.8f, dt, ref vertical);
                    if (s.StageTime > 4f && math.distance(s.Position.xz, prey.Position.xz) < 14f && rng.NextFloat() < dt * 0.6f)
                        NextStage(ref s);
                }
                else if (s.Stage == 2)
                {
                    if (!hasPrey)
                    {
                        NextStage(ref s);
                        return;
                    }
                    var prey = States[p];
                    var g = Terrain.SampleGround(prey.Position.xz);
                    var surface = g.HasWater ? g.WaterSurface : g.Height;
                    var to = prey.Position.xz - s.Position.xz;
                    var distance = math.length(to);
                    var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                    var turn = math.clamp(wanted, -b.TurnRate * 2f * dt, b.TurnRate * 2f * dt);
                    s.Yaw = WrapAngle(s.Yaw + turn);
                    s.YawRate = turn / math.max(dt, 1e-4f);
                    // Steep enough to arrive over the fish as it reaches the water.
                    var drop = math.max(s.Position.y - surface, 0.1f);
                    // Steep (a stoop), but never straight down: at least the minimum airspeed forward, and no
                    // steeper than 70°.
                    s.Speed = math.clamp(distance / math.max(drop / 9f, 0.1f), b.FlySpeed * CreatureMotion.MinAirspeed, b.FlySpeed * 1.3f);
                    s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * s.Speed * dt;
                    s.Position.y = math.max(s.Position.y - math.min(9f, s.Speed * 2.75f) * dt, surface + 0.15f);
                    vertical = -0.85f;
                    if (s.Position.y <= surface + 0.2f || s.StageTime > 6f)
                        NextStage(ref s);
                }
                else
                {
                    // Labouring back up and away.
                    var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                    FlyTo(ref s, ref b, s.Position.xz + forward * 30f, Cruise(s, ref b, s.Position.xz) , b.FlySpeed, dt, ref vertical);
                    vertical = math.max(vertical, 0.35f);
                    if (s.StageTime > 3.5f)
                        AfterHunt(ref s, ref b, ref rng);
                }
            }

            void AfterHunt(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                if (!StartFlight(ref s, ref b, ref rng, false))
                {
                    // Nowhere in particular: down to the ground nearby.
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 1;
                    s.Target = s.Position.xz + rng.NextFloat2Direction() * 15f;
                    s.TargetHeight = Terrain.SampleHeight(s.Target);
                }
            }

            /// <summary>
            /// A fish being dived at: struck if the bird reaches it (within a metre as the bird hits the water). Half
            /// the strikes catch it: the pair decides the same way every time, so the fish and the bird agree.
            /// </summary>
            bool Struck(Entity self, ref CreatureState s)
            {
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (other.Mode == CreatureMode.Hunt && other.Partner == self && other.Stage == 2 &&
                            math.distance(other.Position, s.Position) < 1.1f)
                            return (math.hash(new int2(self.Index, Entities[j].Index)) & 1u) == 0u;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }
        }
    }
}
