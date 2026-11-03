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
        // BrainJob, part: glances, hermits, campers, raids, messengers, folk chat, travel. Split from CreatureBrainSystem.cs by area (#61);
        // the system, Execute and Think live there.
        partial struct BrainJob
        {
            // ------------------------------------------------------------------------------- looking

            /// <summary>
            /// Glances: now and then (prey animals more often), not busy fleeing, hunting, asleep or held, it glances at the
            /// most interesting thing near (within 12 m) and in front of it: another creature (more so one that's busy or
            /// moving fast) or the player (more so sprinting or emoting). It follows that with its head for one to four
            /// seconds (a predator watching its prey, longer), turning no further than 70° round and 35° up or down, then
            /// looks away a while. The target is kept in the state (LookAt and the rest) for anything else that wants it.
            /// </summary>
            void Glance(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float3 player, float playerSpeed,
                        IdleAction emote, ref float desiredLook, out float desiredPitch)
            {
                desiredPitch = 0f;
                var prey = !b.Upright && b.FleeDistance > 0f;
                s.LookCool -= dt;
                if (s.LookFor > 0f)
                {
                    s.LookFor -= dt;
                    if (s.LookKind == 1 && Lookup.TryGetValue(s.LookEntity, out var j))
                        s.LookAt = States[j].Position + new float3(0f, HeadHeight(States[j], ref Bodies[j].Blob.Value), 0f);
                    else if (s.LookKind == 1)
                        s.LookFor = 0f;
                    else if (s.LookKind == 2)
                        s.LookAt = player + new float3(0f, 1.6f, 0f);
                    var to = s.LookAt - (s.Position + new float3(0f, HeadHeight(s, ref b), 0f));
                    var yaw = WrapAngle(math.atan2(to.x, to.z) - s.Yaw);
                    // (Gone round behind it: let it go.)
                    if (math.abs(yaw) > 1.9f)
                        s.LookFor = 0f;
                    else
                    {
                        desiredLook = math.clamp(yaw, -1.22f, 1.22f);
                        desiredPitch = math.clamp(math.atan2(to.y, math.max(math.length(to.xz), 0.1f)), -0.6f, 0.6f);
                    }
                    if (s.LookFor <= 0f)
                        s.LookCool = prey ? rng.NextFloat(1f, 3f) : rng.NextFloat(3f, 8f);
                    return;
                }
                var calm = s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander && !Flying(s) || s.Mode == CreatureMode.Graze ||
                           s.Mode == CreatureMode.Rest || s.Mode == CreatureMode.Sit || s.Mode == CreatureMode.Work || s.Mode == CreatureMode.Greet ||
                           // (A guard on its round looks about too: most of all at someone sprinting or emoting near it.)
                           s.Mode == CreatureMode.Patrol && s.Stage != 2;
                if (s.LookCool > 0f || !calm || rng.NextFloat() >= dt * (prey ? 0.5f : 0.2f))
                    return;
                var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                var best = 0f;
                var target = -1;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self)
                            continue;
                        var o = States[j];
                        var away = o.Position.xz - s.Position.xz;
                        var d = math.length(away);
                        if (d > 12f || d < 0.3f || math.dot(away / d, forward) < 0.1f)
                            continue;
                        var score = (o.Action != IdleAction.None || o.Speed > 2f ? 2f : 1f) / d;
                        if (score > best)
                        {
                            best = score;
                            target = j;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                var toPlayer = player.xz - s.Position.xz;
                var dp = math.length(toPlayer);
                var playerScore = dp < 12f && dp > 0.3f && math.dot(toPlayer / dp, forward) > 0.1f
                    ? (playerSpeed > 4f || emote != IdleAction.None ? 3f : 1.2f) / dp : 0f;
                if (playerScore > best)
                {
                    s.LookKind = 2;
                    s.LookAt = player + new float3(0f, 1.6f, 0f);
                    s.LookFor = rng.NextFloat(1f, 4f);
                }
                else if (target >= 0)
                {
                    s.LookKind = 1;
                    s.LookEntity = Entities[target];
                    s.LookAt = States[target].Position + new float3(0f, HeadHeight(States[target], ref Bodies[target].Blob.Value), 0f);
                    // (A predator keeps its eyes on prey longer.)
                    var watching = false;
                    for (int k = 0; k < b.Prey.Length && !watching; k++)
                        watching = b.Prey[k] == Bodies[target].Species;
                    s.LookFor = watching ? rng.NextFloat(3f, 6f) : rng.NextFloat(1f, 4f);
                }
            }

            /// <summary>
            /// Whether the head is busy with something that moves it itself, so a glance would fight it: preening,
            /// sniffing, grooming, scratching, howling, a head-bob, a petting or carrying pose, grazing or drinking, a
            /// head-down posture, or taking or reading a message. (A child's poke or throw sets its own look.)
            /// </summary>
            static bool HeadBusy(in CreatureState s) =>
                s.Action == IdleAction.Preen || s.Action == IdleAction.Sniff || s.Action == IdleAction.Groom || s.Action == IdleAction.Scratch ||
                s.Action == IdleAction.Howl || s.Action == IdleAction.HeadBob || s.Action == IdleAction.Pet || s.Action == IdleAction.Carry ||
                s.Mode == CreatureMode.Graze || s.Mode == CreatureMode.Drink || s.HeadDown > 0.3f ||
                s.Mode == CreatureMode.Idle && (s.Stage == MessageTake || s.Stage == MessageRead);

            /// <summary>About where a creature's eyes are, above its feet.</summary>
            static float HeadHeight(in CreatureState s, ref CreatureBlob b) => b.StandHeight * math.max(s.Scale, 0.1f) * (b.Upright ? 1.8f : 1.2f);

            // ------------------------------------------------------------------------------- hermits and campers

            /// <summary>The nearest anchor of a kind within reach of a point (its index), or -1.</summary>
            int NearestAnchor(AnchorKind kind, float2 around, float within)
            {
                if (!Terrain.Anchors.IsCreated)
                    return -1;
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(4, Allocator.Temp);
                Anchors.Near(ref set, new float3(around.x, Terrain.SampleHeight(around), around.y), within, kind, ref near);
                var best = -1;
                var d = float.MaxValue;
                foreach (var i in near)
                {
                    var dd = math.distancesq(set.Anchors[i].Position.xz, around);
                    if (dd < d)
                    {
                        d = dd;
                        best = i;
                    }
                }
                near.Dispose();
                return best;
            }

            /// <summary>
            /// What a hermit does next, by the time of day, all round its shelter (its Home is the Home anchor): asleep in
            /// its shelter at night (in by its door, or curled up in an open one); by its fire in the evening; by day
            /// chopping at its woodpile, tending its garden, fetching water, or sitting by its door. False to let it wander.
            /// </summary>
            bool HermitDay(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                // (By the hour, not by sleepiness: a hermit sits up by its fire after dark, then turns in.)
                if (Hour >= 21.5f || Hour < 6f)
                {
                    var door = NearestAnchor(AnchorKind.Door, s.Home, 8f);
                    if (door >= 0)
                    {
                        SetMode(ref s, CreatureMode.Home);
                        s.Anchor = set.Anchors[door].Position;
                        s.AnchorYaw = math.atan2(set.Anchors[door].Facing.x, set.Anchors[door].Facing.y);
                        s.Timer = rng.NextFloat(300f, 600f);
                    }
                    else
                    {
                        // (An open shelter: lying down on its floor.)
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = s.Home;
                        s.Focus = s.Home + new float2(1f, 0f);
                        s.TargetHeight = 3f;
                        s.Timer = rng.NextFloat(300f, 600f);
                    }
                    return true;
                }
                if (Hour >= 17.8f && Hour < 21.5f)
                {
                    var hearth = NearestAnchor(AnchorKind.Hearth, s.Home, 15f);
                    if (hearth >= 0)
                    {
                        var h = set.Anchors[hearth];
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = h.Position.xz + rng.NextFloat2Direction() * (h.Size + 0.5f);
                        s.Focus = h.Position.xz;
                        s.TargetHeight = 1f;
                        s.Timer = rng.NextFloat(60f, 150f);
                        return true;
                    }
                }
                var roll = rng.NextFloat();
                int a;
                if (roll < 0.35f && (a = NearestAnchor(AnchorKind.Work, s.Home, 15f)) >= 0)
                {
                    SetMode(ref s, CreatureMode.Work);
                    s.WorkSpot = set.Anchors[a].Position.xz;
                    s.WorkFace = s.WorkSpot + set.Anchors[a].Facing * 2f;
                    s.AnchorSide = 1;
                    s.Timer = rng.NextFloat(40f, 90f);
                    return true;
                }
                if (roll < 0.62f && (a = NearestAnchor(AnchorKind.Garden, s.Home, 15f)) >= 0)
                {
                    SetMode(ref s, CreatureMode.Work);
                    var g = set.Anchors[a];
                    s.WorkSpot = g.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(0f, g.Size * 0.8f);
                    s.WorkFace = s.WorkSpot + g.Facing * 2f;
                    s.AnchorSide = 2;
                    s.Timer = rng.NextFloat(20f, 45f);
                    return true;
                }
                if (roll < 0.72f && (a = NearestAnchor(AnchorKind.Pool, s.Home, 30f)) >= 0)
                {
                    SetMode(ref s, CreatureMode.Work);
                    s.WorkSpot = set.Anchors[a].Position.xz;
                    s.WorkFace = s.WorkSpot + set.Anchors[a].Facing * 2f;
                    s.AnchorSide = 2;
                    s.Timer = rng.NextFloat(10f, 20f);
                    return true;
                }
                if (roll < 0.9f && (a = NearestAnchor(AnchorKind.Sit, s.Home, 10f)) >= 0)
                {
                    SetMode(ref s, CreatureMode.Sit);
                    var seat = set.Anchors[a];
                    s.Target = seat.Position.xz;
                    s.Focus = seat.Position.xz + seat.Facing * 3f;
                    s.TargetHeight = 4f;
                    s.Anchor = seat.Position;
                    s.Timer = rng.NextFloat(40f, 100f);
                    return true;
                }
                // Otherwise a potter round its place.
                SetMode(ref s, CreatureMode.Wander);
                s.Target = s.Home + rng.NextFloat2Direction() * rng.NextFloat(3f, 10f);
                s.Timer = rng.NextFloat(10f, 25f);
                return true;
            }

            /// <summary>
            /// A hermit doesn't care for company: someone coming within 4.5 m, it backs off a way, waving them off; nearer
            /// than that it watches them (its glances do that).
            /// </summary>
            void Wary(ref CreatureState s, ref Random rng, float3 player)
            {
                if (s.Mode == CreatureMode.Home || s.Mode == CreatureMode.Sleep || s.Mode == CreatureMode.Held || s.Mode == CreatureMode.Wander ||
                    s.Mode == CreatureMode.Sit && s.Stage == 3)
                    return;
                if (math.distance(s.Position.xz, player.xz) > 4.5f)
                    return;
                var away = math.normalizesafe(s.Position.xz - player.xz, rng.NextFloat2Direction());
                SetMode(ref s, CreatureMode.Wander);
                s.Target = s.Position.xz + away * 8f;
                s.Timer = 10f;
                if (s.Action == IdleAction.None)
                {
                    s.Action = IdleAction.Wave;
                    s.ActionTime = 0f;
                    s.ActionLength = 1.4f;
                }
            }

            /// <summary>
            /// A traveller's night at a roadside camp (its Home is the Camp anchor): round the fire through the evening,
            /// on a bedroll (a Bed anchor, lying with its feet to the fire) from late on till morning, then on its way
            /// along the road, done with the camp.
            /// </summary>
            bool CamperNight(ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var camp = NearestAnchor(AnchorKind.Camp, s.Home, 3f);
                if (camp < 0)
                    return false;
                var c = set.Anchors[camp];
                if (Hour >= 6.5f && Hour < 16f)
                {
                    // Morning: off along the road, no longer anyone's camper.
                    s.Task = CreatureTask.None;
                    s.Leash = 0f;
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = c.Position.xz + c.Facing * 120f + new float2(-c.Facing.y, c.Facing.x) * rng.NextFloat(-40f, 40f);
                    s.Timer = 120f;
                    return true;
                }
                if (Hour >= 21.5f || Hour < 6.5f)
                {
                    var beds = new NativeList<int>(4, Allocator.Temp);
                    Anchors.Near(ref set, c.Position, c.Size + 3f, AnchorKind.Bed, ref beds);
                    if (beds.Length > 0)
                    {
                        var bed = set.Anchors[beds[(int)(s.Individual % (uint)beds.Length)]];
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = bed.Position.xz;
                        s.Focus = bed.Position.xz + bed.Facing * 3f;
                        s.TargetHeight = 3f;
                        s.Timer = rng.NextFloat(400f, 800f);
                        beds.Dispose();
                        return true;
                    }
                    beds.Dispose();
                }
                // The evening round the fire.
                var hearth = NearestAnchor(AnchorKind.Hearth, c.Position.xz, c.Size + 2f);
                var middle = hearth >= 0 ? set.Anchors[hearth] : c;
                SetMode(ref s, CreatureMode.Sit);
                math.sincos((s.Individual % 13u) / 13f * 2f * math.PI, out var sn, out var cs);
                s.Target = middle.Position.xz + new float2(sn, cs) * (math.max(middle.Size, 1f) + 0.5f);
                s.Focus = middle.Position.xz;
                s.TargetHeight = 1f;
                s.Timer = rng.NextFloat(60f, 120f);
                return true;
            }

            // ------------------------------------------------------------------------------- hive raids

            const float RaidCooldown = 900f;

            /// <summary>Off to the nearest wild hive within 60 m (to the foot of the trunk under its comb), if there is one.</summary>
            bool StartRaid(ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(2, Allocator.Temp);
                Anchors.Near(ref set, s.Position, 60f, AnchorKind.Hive, ref near);
                var best = -1;
                for (int i = 0; i < near.Length; i++)
                    if (!set.Anchors[near[i]].InCave && (best < 0 || math.distancesq(set.Anchors[near[i]].Position, s.Position) <
                                                                     math.distancesq(set.Anchors[best].Position, s.Position)))
                        best = near[i];
                near.Dispose();
                if (best < 0)
                    return false;
                var hive = set.Anchors[best];
                var trunk = math.max(hive.Size - 1f, 0.1f);
                SetMode(ref s, CreatureMode.Work);
                s.Stage = CreatureAnimator.RaidWalk;
                s.Focus = hive.Position.xz + hive.Facing * trunk;
                s.Target = hive.Position.xz + hive.Facing * (trunk + 0.05f);
                s.Timer = 120f;
                // (Even if it never gets there: no other raid for a long while.)
                s.ChaseCooldown = RaidCooldown;
                return true;
            }

            /// <summary>
            /// A bear after a hive's honey: walks to the foot of the trunk under the comb (RaidWalk); rears up at it, pawing
            /// and swaying, while the bees mob it (RaidAtComb); then makes off, running at first, to somewhere 70 to 100 m
            /// from the hive (RaidLeave), and takes up its day there.
            /// </summary>
            void Raiding(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run,
                         ref float targetSpeed, ref bool moving, ref float sit, ref float vertical, ref float desiredLook, ref float headDown, ref float bow)
            {
                s.Timer -= dt;
                if (s.Stage == CreatureAnimator.RaidWalk)
                {
                    targetSpeed = walk;
                    moving = true;
                    if (math.distance(s.Position.xz, s.Target) < 0.4f)
                    {
                        NextStage(ref s);
                        s.Timer = rng.NextFloat(6f, 10f);
                    }
                    else if (s.Timer <= 0f)
                        Pause(ref s, ref b, ref rng);
                }
                else if (s.Stage == CreatureAnimator.RaidAtComb && b.Upright)
                {
                    // One of the folk after the honey: stood at the trunk poking up at the comb with a stick (the
                    // animator's pose) while the bees boil out, till a piece comes down; then off with it at a run.
                    s.Speed = 0f;
                    var to = s.Focus - s.Position.xz;
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                    desiredLook = 0.15f * math.sin(s.StageTime * 3.1f);
                    if (s.Timer <= 0f)
                    {
                        s.Held = HeldItem.Honey;
                        var away = math.normalizesafe(s.Position.xz - s.Focus, rng.NextFloat2Direction());
                        var spot = s.Position.xz + away * rng.NextFloat(80f, 110f);
                        for (int tries = 0; tries < 4 && !Walkable(spot, Habitat.Land); tries++)
                            spot = s.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(80f, 110f);
                        s.Stage = CreatureAnimator.RaidLeave;
                        s.StageTime = 0f;
                        s.Target = spot;
                        s.Timer = 120f;
                    }
                }
                else if (s.Stage == CreatureAnimator.RaidAtComb)
                {
                    s.Speed = 0f;
                    // (Shuffling the last of the way in, up against the bark.)
                    var short_ = s.Target - s.Position.xz;
                    s.Position.xz += math.normalizesafe(short_) * math.min(math.length(short_), 0.5f * dt);
                    var to = s.Focus - s.Position.xz;
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                    // Up on its hind legs against the trunk, leaning in, front paws braced up it and scraping at it,
                    // head tipped up to the comb.
                    sit = 0.6f;
                    vertical = 0.78f + 0.06f * math.sin(s.StageTime * 5f);
                    desiredLook = 0.25f * math.sin(s.StageTime * 2.3f);
                    // Down comes a chunk of comb: down on all fours to lick at it.
                    if (s.Timer <= 0f)
                    {
                        s.Stage = CreatureAnimator.RaidLick;
                        s.StageTime = 0f;
                        s.Held = HeldItem.Honey;
                        s.Timer = rng.NextFloat(4f, 6f);
                    }
                }
                else if (s.Stage == CreatureAnimator.RaidLick)
                {
                    // Licking at it, nose down to it (the front end bowed: a bear has no neck to speak of), till the bees
                    // drive it off.
                    s.Speed = 0f;
                    headDown = 1f;
                    bow = 0.8f + 0.2f * math.sin(s.StageTime * 7f);
                    desiredLook = 0.2f * math.sin(s.StageTime * 6f);
                    if (s.Timer <= 0f)
                    {
                        s.Held = HeldItem.None;
                        // Away from the hive, a little to one side or the other.
                        var away = math.normalizesafe(s.Position.xz - s.Focus, rng.NextFloat2Direction());
                        var turn = rng.NextFloat(-0.6f, 0.6f);
                        math.sincos(turn, out var sn, out var cs);
                        away = new float2(away.x * cs - away.y * sn, away.x * sn + away.y * cs);
                        var spot = s.Position.xz + away * rng.NextFloat(70f, 100f);
                        for (int tries = 0; tries < 4 && !Walkable(spot, Habitat.Land); tries++)
                            spot = s.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(70f, 100f);
                        s.Stage = CreatureAnimator.RaidLeave;
                        s.StageTime = 0f;
                        s.Target = spot;
                        s.Timer = 120f;
                    }
                }
                else
                {
                    // (Running from the bees at first; one of the folk runs further, flapping at them.)
                    targetSpeed = s.StageTime < (b.Upright ? 12f : 6f) ? run : walk;
                    moving = true;
                    if (math.distance(s.Position.xz, s.Target) < 2f || s.Timer <= 0f)
                        Pause(ref s, ref b, ref rng);
                }
            }

            // ------------------------------------------------------------------------------- messenger birds

            /// <summary>Idle-mode stages of one of the townsfolk a messenger bird has come to: waiting for it, taking the
            /// message, reading it.</summary>
            const byte MessageWait = CreatureAnimator.MessageWait, MessageTake = CreatureAnimator.MessageTake, MessageRead = CreatureAnimator.MessageRead;

            /// <summary>Where a messenger bird perches on the forearm one of the townsfolk holds up for it (see the
            /// animator's MessageWait pose): out in front of them at chest height, a little to their left.</summary>
            float3 Perch(in CreatureState them, ref CreatureBlob tb)
            {
                var f = new float2(math.sin(them.Yaw), math.cos(them.Yaw));
                var left = new float2(-f.y, f.x);
                var scale = math.max(them.Scale, 0.3f);
                var xz = them.Position.xz + f * 0.42f * scale + left * 0.12f * scale;
                return new float3(xz.x, them.Position.y + tb.StandHeight * scale * PerchHeight, xz.y);
            }

            const float PerchHeight = 1.3f;

            /// <summary>
            /// A messenger bird with a message: in over the town to one of the townsfolk about the place (the nearest to
            /// it, re-chosen if that one goes in), down to land on the forearm they hold up for it (0); there, waiting while they
            /// take it (1); then up and away towards the town it's making for next (2), where it's an ordinary bird again.
            /// </summary>
            void Messenger(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float vertical)
            {
                var hasThem = Lookup.TryGetValue(s.Partner, out var r);
                if (s.Stage == 0)
                {
                    if (!hasThem || !Receiver(States[r], ref Bodies[r].Blob.Value, s.Home) && !(States[r].Mode == CreatureMode.Idle && States[r].Stage == MessageWait))
                    {
                        // (Nobody free just now: round over the town, looking again now and then, for two minutes.)
                        s.Partner = rng.NextFloat() < dt * 2f ? FindReceiver(s.Position.xz, s.Home) : Entity.Null;
                        if (s.StageTime > 120f)
                        {
                            NextStage(ref s);
                            s.Stage = 2;
                        }
                        var angle = s.StageTime * 0.25f;
                        var round = s.Home + new float2(math.cos(angle), math.sin(angle)) * 15f;
                        FlyTo(ref s, ref b, round, Cruise(s, ref b, round) + 8f, b.FlySpeed, dt, ref vertical);
                        return;
                    }
                    var perch = Perch(States[r], ref Bodies[r].Blob.Value);
                    if (FlyTo(ref s, ref b, perch.xz, perch.y, b.FlySpeed, dt, ref vertical))
                        NextStage(ref s);
                    else if (s.StageTime > 120f)
                    {
                        NextStage(ref s);
                        s.Stage = 2;
                    }
                }
                else if (s.Stage == 1)
                {
                    // On the forearm (kept there as it moves), facing them.
                    s.Speed = 0f;
                    if (hasThem)
                    {
                        s.Position = Perch(States[r], ref Bodies[r].Blob.Value);
                        var to = States[r].Position.xz - s.Position.xz;
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                    }
                    // Off once the message is taken (or nobody comes for it).
                    var taken = hasThem && States[r].Mode == CreatureMode.Idle && States[r].Stage == MessageRead;
                    if (taken)
                        s.Held = HeldItem.None;
                    if (taken || s.StageTime > 10f)
                        NextStage(ref s);
                }
                else
                {
                    if (FlyTo(ref s, ref b, s.WorkSpot, Cruise(s, ref b, s.WorkSpot) + 10f, b.FlySpeed, dt, ref vertical))
                    {
                        s.Task = CreatureTask.None;
                        s.Held = HeldItem.None;
                        Pause(ref s, ref b, ref rng);
                    }
                }
            }

            /// <summary>One of the townsfolk who could take a message: grown, about the town (not indoors, asleep, at a post
            /// or on guard), near its middle.</summary>
            static bool Receiver(in CreatureState o, ref CreatureBlob ob, float2 town) =>
                ob.Upright && ob.Hands && !ob.Child && o.Leash > 0f && (Free(o.Mode) || o.Mode == CreatureMode.Greet) &&
                !(o.Mode == CreatureMode.Idle && o.Stage >= MessageWait) &&
                o.Task != CreatureTask.Patrol && o.Task != CreatureTask.Watch && math.distance(o.Position.xz, town) < 60f;

            /// <summary>The one of the townsfolk nearest the bird who could take its message, or none.</summary>
            Entity FindReceiver(float2 from, float2 town)
            {
                var best = Entity.Null;
                var d = float.MaxValue;
                for (int j = 0; j < States.Length; j++)
                {
                    if (!Receiver(States[j], ref Bodies[j].Blob.Value, town))
                        continue;
                    var dj = math.distancesq(States[j].Position.xz, from);
                    if (dj < d)
                    {
                        d = dj;
                        best = Entities[j];
                    }
                }
                return best;
            }

            /// <summary>A messenger bird coming down to this one (within a cell or so).</summary>
            bool FindMessenger(Entity self, in CreatureState s, out Entity bird)
            {
                bird = Entity.Null;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var o = States[j];
                        if (o.Task == CreatureTask.Messenger && o.Mode == CreatureMode.Work && o.Stage <= 1 && o.Partner == self &&
                            math.distance(o.Position.xz, s.Position.xz) < 25f)
                        {
                            bird = Entities[j];
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>
            /// One of the townsfolk a messenger bird has come to: stands and turns to it as it comes down, holding up a
            /// forearm for it to land on (60); once it's on it, takes the message from its leg with the other hand (61);
            /// then reads it a while (62), and goes about its day.
            /// </summary>
            void Receiving(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                var hasBird = Lookup.TryGetValue(s.Partner, out var j);
                var bird = hasBird ? States[j] : default;
                // (Eyes on the bird, as it comes in and while it's there.)
                if (hasBird && s.Stage < MessageRead)
                {
                    s.LookKind = 1;
                    s.LookEntity = s.Partner;
                    s.LookFor = math.max(s.LookFor, 0.3f);
                }
                // (Turned to the bird as it comes in; holding still for the last few metres, so it has a steady arm to land on.)
                if (hasBird)
                {
                    var to = bird.Position.xz - s.Position.xz;
                    if (s.Stage == MessageWait && math.lengthsq(to) > 9f)
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                }
                if (s.Stage == MessageWait)
                {
                    s.Timer -= dt;
                    if (!hasBird || bird.Task != CreatureTask.Messenger || bird.Stage == 2 || s.Timer <= 0f)
                        Pause(ref s, ref b, ref rng);
                    else if (bird.Stage == 1 && math.distance(bird.Position.xz, s.Position.xz) < 1.5f)
                        NextStage(ref s);
                }
                else if (s.Stage == MessageTake)
                {
                    if (s.StageTime > 1.4f)
                    {
                        NextStage(ref s);
                        s.Timer = rng.NextFloat(8f, 14f);
                    }
                }
                else
                {
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                        Pause(ref s, ref b, ref rng);
                }
            }

            // ------------------------------------------------------------------------------- talking to each other

            // (A-26. Idle stages, like the messenger's: the Stage byte is all a client sees, and the animator's gestures
            // and the speech bubble come from it. Who's with whom (Partner, ChatRole) stays on the host; a talk shared
            // with clients in full would need the partner sent as well.)
            const byte ChatTalk = CreatureAnimator.ChatTalk, ChatListen = CreatureAnimator.ChatListen;
            /// <summary>At most this many pairs talk at once round a town; a talk lasts 4-10 s; then each waits this long before another.</summary>
            const int MaxChats = 3;
            const float ChatCoolMin = 90f, ChatCoolMax = 200f;
            // (How far apart two grown folk can be for one to stop the other for a talk: they walk up to each other.)
            const float ChatReach = 30f;

            static bool InChat(in CreatureState o) => o.Mode == CreatureMode.Idle && (o.Stage == ChatTalk || o.Stage == ChatListen);

            /// <summary>
            /// Now and then, one of the townsfolk free to talk: stops for someone who stopped for it (2); a grown one, every
            /// 40 s or so with another free within ChatReach, stops it for a talk (1: they walk up to each other), unless a few pairs round the town already
            /// are; a child, now and then, goes to stand by a talk under way (3).
            /// </summary>
            void StartChat(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                var forced = ForceChat.Data != 0;
                // (#57: the roll is still drawn every frame, so the rest of its random draws don't shift.)
                var askRoll = rng.NextFloat() < 0.3f;
                if ((SearchEveryFrame.Data != 0 ? askRoll : SearchDue(in s)) && FindChat(self, in s, 0, ChatReach, out var asker))
                {
                    BeginChat(ref s, asker, ChatListen, 2);
                    return;
                }
                if (s.ChatCool > 0f)
                    return;
                if (b.Child)
                {
                    if (rng.NextFloat() < dt * (forced ? 0.3f : 0.05f) && FindChat(self, in s, 2, forced ? 14f : 8f, out var adult))
                        BeginChat(ref s, adult, ChatListen, 3);
                    return;
                }
                // (Up to ChatReach, as far as the other looks for one who stopped for it: once a town settles its few folk are
                // spread over its posts and streets, and seldom pass within a few metres; A-27.)
                if (rng.NextFloat() >= dt * (forced ? 1f : 1f / 40f) || !FindChat(self, in s, 1, ChatReach, out var other) ||
                    ChatsNear(math.all(s.Home == float2.zero) ? s.Position.xz : s.Home) >= MaxChats)
                    return;
                BeginChat(ref s, other, ChatTalk, 1);
                s.Timer = rng.NextFloat(4f, 10f);
            }

            /// <summary>
            /// Stops for a talk, keeping what it was doing in ChatWalk / ChatResume: 1 walking somewhere (ChatResume the
            /// target), 2 on its way to or at its post (ChatResume.x the time left at work: Timer is the talk's while it lasts),
            /// 3 on its way to a seat (it picks something new after), 0 standing about.
            /// </summary>
            static void BeginChat(ref CreatureState s, Entity with, byte stage, byte role)
            {
                s.ChatWalk = (byte)(s.Mode == CreatureMode.Wander ? 1 : s.Mode == CreatureMode.Work ? 2 : s.Mode == CreatureMode.Sit ? 3 : 0);
                s.ChatResume = s.ChatWalk == 2 ? new float2(s.Timer, 0f) : s.Target;
                SetMode(ref s, CreatureMode.Idle);
                s.Stage = stage;
                s.Partner = with;
                s.ChatRole = role;
            }

            /// <summary>After a talk, on to wherever it was going (or a pause where it stood), not to talk again for a while.</summary>
            static void EndChat(ref CreatureState s, ref CreatureBlob b, ref Random rng, float cool)
            {
                var to = s.ChatResume;
                if (s.ChatWalk == 1)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = to;
                }
                else if (s.ChatWalk == 2)
                {
                    // (On to its post, with the time at work it had left.)
                    SetMode(ref s, CreatureMode.Work);
                    s.Target = s.WorkSpot;
                    s.Timer = math.max(to.x, 5f);
                }
                else
                    Pause(ref s, ref b, ref rng);
                s.ChatRole = 0;
                s.ChatWalk = 0;
                s.ChatCool = cool;
            }

            /// <summary>
            /// Nearest within reach: (0) one who stopped for this one; (1) a grown one free to stop for a talk (only toward
            /// a later entity, so two don't stop each other at once); (2) a grown one who started a talk, for a child.
            /// </summary>
            bool FindChat(Entity self, in CreatureState s, int kind, float reach, out Entity found)
            {
                if (kind == 0)
                    System.Threading.Interlocked.Increment(ref Searches.Data.Chat);
                found = Entity.Null;
                var best = reach * reach;
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(reach / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var o = States[j];
                        var d = math.distancesq(o.Position.xz, s.Position.xz);
                        if (d >= best || Entities[j] == self)
                            continue;
                        ref var ob = ref Bodies[j].Blob.Value;
                        var ok = kind == 0 ? InChat(o) && o.ChatRole == 1 && o.Partner == self
                               : kind == 1 ? !ob.Child && o.ChatRole == 0 && o.ChatCool <= 0f && Entities[j].Index > self.Index && ChatFree(in o, ref ob)
                               : InChat(o) && o.ChatRole == 1 && !ob.Child;
                        if (ok)
                        {
                            best = d;
                            found = Entities[j];
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found != Entity.Null;
            }

            /// <summary>Talks under way within 60 m (one per pair: those who started them).</summary>
            int ChatsNear(float2 at)
            {
                var n = 0;
                for (int j = 0; j < States.Length; j++)
                    if (States[j].ChatRole == 1 && math.distancesq(States[j].Position.xz, at) < 3600f)
                        n++;
                return n;
            }

            /// <summary>
            /// Talking: the two walk up to each other (true while walking) and stand a little over a metre apart, turned to
            /// each other and looking at each other, one talking (ChatTalk: hands going) while the other listens and nods
            /// (ChatListen), changing over every few seconds; a child by them listens. The one who started it ends it after
            /// its 4-10 s; the other (and the child) when it sees that.
            /// </summary>
            bool Chatting(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                var forced = ForceChat.Data != 0;
                var cool = forced ? rng.NextFloat(15f, 30f) : rng.NextFloat(ChatCoolMin, ChatCoolMax);
                var has = Lookup.TryGetValue(s.Partner, out var j);
                var o = has ? States[j] : default;
                var joined = s.ChatRole == 1 ? has && InChat(o) && o.ChatRole == 2 && o.Partner == self
                                             : has && InChat(o) && o.ChatRole == 1 && (s.ChatRole == 3 || o.Partner == self);
                if (!joined && (s.ChatRole != 1 || s.ModeTime > 2.5f) || s.ModeTime > 30f)
                {
                    // (Not taken up within a moment: a shorter wait before trying again.)
                    EndChat(ref s, ref b, ref rng, s.ChatRole == 1 && !joined ? cool * 0.2f : cool);
                    return false;
                }
                var to = o.Position.xz - s.Position.xz;
                var gap = math.length(to);
                var want = s.ChatRole == 3 ? 1.9f : 1.25f;
                s.LookKind = 1;
                s.LookEntity = s.Partner;
                s.LookFor = math.max(s.LookFor, 0.3f);
                var spare = gap - want;
                if (joined && spare > 0.15f)
                {
                    s.Target = o.Position.xz - to / math.max(gap, 0.01f) * (want * 0.5f);
                    // (Slowing as the gap closes: both walk, so stopping from full speed carried them into each other; A-28.)
                    s.Speed = math.min(s.Speed, math.max(0.25f, spare * 1.2f));
                    return true;
                }
                // (Stood: no carrying on into the other.)
                s.Speed = math.min(s.Speed, math.max(0f, spare));
                if (joined && spare < -0.15f)
                {
                    // Too close (they met at a well or a seat, A-28): each steps back, still facing the other, half the
                    // shortfall each. (On top of each other: the one who started it steps one way, the other the other.)
                    var away = gap > 0.05f ? -to / gap : new float2(0f, s.ChatRole == 1 ? -1f : 1f);
                    var next = s.Position.xz + away * math.min(0.9f * dt, -spare * 0.5f);
                    if (Walkable(next, b.Habitat))
                        s.Position.xz = next;
                }
                if (gap > 0.01f)
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                if (!joined)
                    return false;
                if (s.ChatRole == 1)
                {
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                    {
                        EndChat(ref s, ref b, ref rng, cool);
                        return false;
                    }
                    // (Turn about: after a couple of seconds, now and then, the other's turn.)
                    if (s.StageTime > 1.8f && rng.NextFloat() < dt / 0.8f)
                    {
                        s.Stage = s.Stage == ChatTalk ? ChatListen : ChatTalk;
                        s.StageTime = 0f;
                    }
                }
                else if (s.ChatRole == 2 && gap < want + 0.5f)
                {
                    var stage = o.Stage == ChatTalk ? ChatListen : ChatTalk;
                    if (s.Stage != stage)
                    {
                        s.Stage = stage;
                        s.StageTime = 0f;
                    }
                }
                return false;
            }

            // ------------------------------------------------------------------------------- travelling

            /// <summary>
            /// A traveller on the road: along it stretch by stretch (its stretch in TargetHeight, which way in
            /// AnchorSide: 1 towards each stretch's B), at a brisk walk; at a fork, on along whichever stretch carries on
            /// from where it stands (any but the way it came). Where the road ends (in the town it was making for) it has
            /// arrived: it stays a while there as a visitor, the town's streets its home.
            /// </summary>
            void Travelling(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk)
            {
                if (!Terrain.Settlements.IsCreated)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                ref var set = ref Terrain.Settlements.Value;
                var k = (int)s.TargetHeight;
                if (k < 0 || k >= set.Streets.Length)
                {
                    Arrived(ref s, ref b, ref rng);
                    return;
                }
                var onward = s.AnchorSide == 1;
                var goal = onward ? set.Streets[k].B : set.Streets[k].A;
                if (WalkOn(ref s, ref b, goal, walk * 1.15f, dt) > 0.8f)
                    return;
                // The next stretch: the neighbour in the list, if it carries on from here, else any road stretch that does.
                var next = -1;
                var nextOnward = onward;
                var n = onward ? k + 1 : k - 1;
                if (n >= 0 && n < set.Streets.Length && set.Streets[n].Road != 0 &&
                    math.distancesq((onward ? set.Streets[n].A : set.Streets[n].B).xz, goal.xz) < 2f)
                    next = n;
                for (int i = 0; i < set.Streets.Length && next < 0; i++)
                {
                    ref var st = ref set.Streets[i];
                    if (i == k || st.Road == 0)
                        continue;
                    if (math.distancesq(st.A.xz, goal.xz) < 2f)
                    {
                        next = i;
                        nextOnward = true;
                    }
                    else if (math.distancesq(st.B.xz, goal.xz) < 2f)
                    {
                        next = i;
                        nextOnward = false;
                    }
                }
                if (next < 0)
                {
                    Arrived(ref s, ref b, ref rng);
                    return;
                }
                s.TargetHeight = next;
                s.AnchorSide = (byte)(nextOnward ? 1 : 0);
            }

            /// <summary>A traveller at its road's end: a visitor in the town now, about its streets for a while.</summary>
            void Arrived(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                s.Task = CreatureTask.None;
                s.Home = s.Position.xz;
                s.Leash = 30f;
                s.Held = HeldItem.None;
                Pause(ref s, ref b, ref rng);
            }
        }
    }
}
