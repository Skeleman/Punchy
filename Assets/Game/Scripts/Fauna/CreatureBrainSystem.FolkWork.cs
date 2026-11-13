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
        // BrainJob, part: mining, swimming, fishing, patrols, doors, folk hunts, holding. Split from CreatureBrainSystem.cs by area (#61);
        // the system, Execute and Think live there.
        partial struct BrainJob
        {
            // ------------------------------------------------------------------------------- mining

            /// <summary>
            /// A miner's day: from its camp to the mouth of the nearest mine (stage 1), in along the passage node by node
            /// (10 + node), to the ore face (100) and there swinging its pick a while (101: MinerAtFace); then a sack of
            /// ore out again node by node (110 + node), up out of the mouth and back to the camp (200), where it sets the
            /// sack down (201) and goes back for more. At dusk it makes its way out, without a load, and home. Its mine
            /// and its face (its own, chosen by its individual number, the same each day) are its LairCave and
            /// LairNode, the face's spot its Anchor. It walks itself, on the floor under it: the cave's in the mine,
            /// the ground outside.
            /// </summary>
            void Mining(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk)
            {
                if (!Terrain.Caves.IsCreated)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                if (s.Stage == 0)
                {
                    if (!FindOreFace(ref s))
                    {
                        Pause(ref s, ref b, ref rng);
                        return;
                    }
                    s.Stage = 1;
                    s.StageTime = 0f;
                    s.Held = HeldItem.Pickaxe;
                }
                ref var set = ref Terrain.Caves.Value;
                ref var cave = ref set.Caves[s.LairCave];
                var dusk = Night > 0.45f;
                var pace = walk * (s.Held == HeldItem.OreSack ? 0.8f : 1f);
                // (At dusk, on the way in or at the face: turn about and make for the way out, as it is.)
                if (dusk && (s.Stage >= 10 && s.Stage <= 101))
                {
                    var at = s.Stage == 100 || s.Stage == MinerAtFace ? s.LairNode : s.Stage - 10;
                    s.Stage = (byte)(110 + math.clamp(at, 0, 88));
                    s.StageTime = 0f;
                }
                if (s.Stage == 1)
                {
                    if (WalkOn(ref s, ref b, cave.Mouth, pace, dt) < 1f)
                        Next(ref s, 11);
                }
                else if (s.Stage >= 10 && s.Stage < 100)
                {
                    var node = s.Stage - 10;
                    if (WalkOn(ref s, ref b, set.Nodes[cave.FirstNode + node].Position, pace, dt) < 1.2f)
                        Next(ref s, node >= s.LairNode ? (byte)100 : (byte)(s.Stage + 1));
                }
                else if (s.Stage == 100)
                {
                    if (WalkOn(ref s, ref b, s.Anchor, pace, dt) < 0.4f)
                    {
                        Next(ref s, MinerAtFace);
                        s.Timer = rng.NextFloat(40f, 90f);
                    }
                }
                else if (s.Stage == MinerAtFace)
                {
                    // At the face, swinging at it (the swing is the Mine task's pose), till the sack's full.
                    s.Speed = 0f;
                    var wanted = WrapAngle(s.AnchorYaw - s.Yaw);
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                    {
                        s.Held = HeldItem.OreSack;
                        Next(ref s, (byte)(110 + math.clamp(s.LairNode, 0, 88)));
                    }
                }
                else if (s.Stage >= 110 && s.Stage < 199)
                {
                    var node = s.Stage - 110;
                    if (WalkOn(ref s, ref b, set.Nodes[cave.FirstNode + node].Position, pace, dt) < 1.2f)
                        Next(ref s, node <= 1 ? (byte)199 : (byte)(s.Stage - 1));
                }
                else if (s.Stage == 199)
                {
                    if (WalkOn(ref s, ref b, cave.Mouth, pace, dt) < 1f)
                        Next(ref s, 200);
                }
                else if (s.Stage == 200)
                {
                    var home = new float3(s.Home.x, Terrain.SampleHeight(s.Home), s.Home.y);
                    if (WalkOn(ref s, ref b, home, pace, dt) < 1.5f)
                    {
                        Next(ref s, 201);
                        s.Timer = rng.NextFloat(3f, 6f);
                    }
                }
                else
                {
                    // Setting the sack down at the camp; back for more while it's day, else done for the day.
                    s.Speed = 0f;
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                    {
                        s.Held = HeldItem.None;
                        if (dusk)
                            Pause(ref s, ref b, ref rng);
                        else
                        {
                            s.Held = HeldItem.Pickaxe;
                            Next(ref s, 1);
                        }
                    }
                }
                // (A stage that can't be got through in two minutes, four for the walks between the camp and the mine,
                // it gives up on: home, and try again later.)
                var limit = s.Stage == 1 || s.Stage == 200 ? 240f : 120f;
                if (s.Mode == CreatureMode.Work && s.Stage != MinerAtFace && s.Stage != 201 && s.StageTime > limit)
                {
                    s.Held = HeldItem.None;
                    Pause(ref s, ref b, ref rng);
                }

                static void Next(ref CreatureState s, byte stage)
                {
                    s.Stage = stage;
                    s.StageTime = 0f;
                }
            }

            const byte MinerAtFace = CreatureAnimator.MinerAtFace;

            /// <summary>
            /// The ore face a miner works (one of the mines' Work anchors in a chamber, not the one by the mouth, within
            /// 400 m of its camp; which one by its individual number), and its mine and the chamber's node; false if
            /// there's none.
            /// </summary>
            bool FindOreFace(ref CreatureState s)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var anchors = ref Terrain.Anchors.Value;
                ref var caves = ref Terrain.Caves.Value;
                var found = new NativeList<int>(16, Allocator.Temp);
                Anchors.Near(ref anchors, new float3(s.Home.x, s.Position.y, s.Home.y), 400f, AnchorKind.Work, ref found);
                var faces = new FixedList128Bytes<int>();
                for (int i = 0; i < found.Length && faces.Length < 30; i++)
                {
                    var a = anchors.Anchors[found[i]];
                    if (a.InCave && a.Size < 1.2f && a.OwnerIndex < caves.Caves.Length)
                        faces.Add(found[i]);
                }
                found.Dispose();
                if (faces.Length == 0)
                    return false;
                var face = anchors.Anchors[faces[(int)(s.Individual % (uint)faces.Length)]];
                ref var cave = ref caves.Caves[face.OwnerIndex];
                var best = float.MaxValue;
                var node = 1;
                for (int n = 1; n < cave.NodeCount; n++)
                {
                    var d = math.distance(caves.Nodes[cave.FirstNode + n].Position, face.Position);
                    if (d < best)
                    {
                        best = d;
                        node = n;
                    }
                }
                s.LairCave = (short)face.OwnerIndex;
                s.LairNode = (short)math.min(node, 88);
                s.Anchor = face.Position;
                s.AnchorYaw = math.atan2(face.Facing.x, face.Facing.y);
                return true;
            }

            /// <summary>
            /// A step towards a point at a walking pace, turning to face the way, on the floor under it (the cave's
            /// under rock, the ground's outside); how far it still is, across.
            /// </summary>
            float WalkOn(ref CreatureState s, ref CreatureBlob b, float3 target, float speed, float dt)
            {
                var to = target.xz - s.Position.xz;
                var d = math.length(to);
                s.Speed = speed;
                CreatureMotion.Accelerate(ref s, ref b, dt);
                speed = s.Speed;
                if (d > 1e-3f)
                {
                    s.Position.xz += to / d * math.min(d, speed * dt);
                    var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                }
                s.Speed = d > 0.05f ? speed : 0f;
                // (From a little above where it is, or where it's going if that's higher: up a slope, out of a pit.)
                var from = new float3(s.Position.x, math.max(s.Position.y, target.y) + 0.8f, s.Position.z);
                s.Position.y = Terrain.FloorUnder(from, out _);
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
                return d;
            }

            // ------------------------------------------------------------------------------- swimming

            /// <summary>
            /// Water good for a swim near where it is: calm (no rapids), deep enough to swim (1.6 m), not lava or a
            /// cave's hole, and warm (no snow on the bank). Sets off to the bank there (WorkSpot) with the middle of the
            /// swim at Focus. False if there's none within 60 m. Water folk (Wades) take to the shallows: from 0.5 m deep.
            /// </summary>
            bool StartSwim(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                var deep = b.Wades > 0f ? 0.5f : 1.6f;
                var start = rng.NextFloat(math.PI * 2f);
                for (var d = 6f; d <= 60f; d += 6f)
                    for (int k = 0; k < 12; k++)
                    {
                        var a = start + k * math.PI / 6f;
                        var dir = new float2(math.sin(a), math.cos(a));
                        var p = s.Position.xz + dir * d;
                        var g = Terrain.SampleGround(p);
                        if (!g.HasWater || g.WaterSurface - g.Height < deep || g.Rapids > 0.15f || !Swimmable(p))
                            continue;
                        var q = p;
                        for (int step = 0; step < 40 && !Walkable(q, Habitat.Land); step++)
                            q -= dir * 0.5f;
                        if (!Walkable(q, Habitat.Land) || Terrain.SampleSurface(q, Terrain.SampleGround(q), Terrain.SampleNormal(q, 1f)).Snow > 0.2f)
                            continue;
                        SetMode(ref s, CreatureMode.Swim);
                        s.WorkSpot = q;
                        s.Focus = p;
                        s.Target = q;
                        s.Timer = rng.NextFloat(60f, 150f);
                        return true;
                    }
                return false;
            }

            /// <summary>
            /// Out for a swim: to the bank (0), in and swimming about from point to point in the deep water round Focus
            /// at a gentle breaststroke (1), back to the bank when the time's up or the day draws in (2), and standing
            /// on the bank a while drying off, looking about (3). Out in the deep water they dive (Diving).
            /// </summary>
            void Swimming(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float idleLook,
                          ref float targetSpeed, ref bool moving, ref float desiredLook, ref float vertical)
            {
                Diving(ref s, ref b, ref rng, dt, s.Stage == 1, ref vertical);
                switch (s.Stage)
                {
                    case 0:
                        s.Target = s.WorkSpot;
                        targetSpeed = walk;
                        moving = true;
                        if (math.distance(s.Position.xz, s.WorkSpot) < 0.8f)
                        {
                            NextStage(ref s);
                            s.Target = s.Focus;
                        }
                        break;
                    case 1:
                        s.Timer -= dt;
                        targetSpeed = 0.9f * s.Scale;
                        moving = true;
                        if (math.distance(s.Position.xz, s.Target) < 1f)
                        {
                            // Somewhere else in the deep water round the middle.
                            for (int k = 0; k < 6; k++)
                            {
                                var p = s.Focus + rng.NextFloat2Direction() * rng.NextFloat(2f, 10f);
                                if (WaterDepth(p) > (b.Wades > 0f ? 0.35f : 1.3f) && Swimmable(p))
                                {
                                    s.Target = p;
                                    break;
                                }
                            }
                        }
                        if (s.Timer <= 0f || Night > 0.4f)
                        {
                            NextStage(ref s);
                            s.Target = s.WorkSpot;
                        }
                        break;
                    case 2:
                        s.Target = s.WorkSpot;
                        targetSpeed = 0.9f * s.Scale;
                        moving = true;
                        if (math.distance(s.Position.xz, s.WorkSpot) < 0.8f)
                        {
                            NextStage(ref s);
                            s.Timer = rng.NextFloat(15f, 35f);
                        }
                        break;
                    default:
                        s.Timer -= dt;
                        desiredLook = idleLook;
                        if (s.Timer <= 0f)
                            Pause(ref s, ref b, ref rng);
                        break;
                }
            }

            /// <summary>
            /// A-102 row 2: folk out swimming dive (CreatureMind.Dive, below the height they float at; Advance puts them
            /// there). An air breather goes down only with its breath full, as deep as Breath.MaxDive allows and the
            /// water has room for, swims about down there, and turns for the surface while what's left still gets it up
            /// (Breath.TimeToRise), then rests at the surface until its breath is back and a while more. A water
            /// breather (sea folk) dives as deep as the water goes, stays a while at each depth and sometimes comes up.
            /// Head down going down, up coming up (Vertical, which clients get). Only out in the deep water (dive is
            /// false wading in and out, so they come up first); gasping (Breathe), always up.
            /// </summary>
            void Diving(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, bool dive, ref float vertical)
            {
                var room = WaterDepth(s.Position.xz) - 0.6f * s.Scale;
                var sea = b.BreathesWater;
                if (!math.isfinite(M.Dive + M.DiveTo + M.DiveWait))
                    M.Dive = M.DiveTo = M.DiveWait = 0f;
                if (!dive || M.Gasping || room < 0.6f * s.Scale)
                    M.DiveTo = 0f;
                else if (M.DiveTo <= 0f)
                {
                    // At the surface (or on the way up): resting till its breath is back, then down again.
                    if (M.Dive < 0.05f)
                        M.DiveWait -= dt;
                    var most = sea ? room : math.min(room, Breath.MaxDive(b.HoldBreath));
                    if (M.Dive < 0.05f && M.DiveWait <= 0f && (sea || M.BreathUsed <= 0f) && most >= 0.6f * s.Scale)
                    {
                        M.DiveTo = rng.NextFloat(math.min(0.6f * s.Scale, most), most);
                        M.DiveWait = rng.NextFloat(4f, 12f);
                    }
                }
                else
                {
                    M.DiveTo = math.min(M.DiveTo, room);
                    if (!sea && Breath.TimeToRise(M.BreathUsed, M.Dive, b.HoldBreath))
                    {
                        M.DiveTo = 0f;
                        M.DiveWait = rng.NextFloat(3f, 10f);
                    }
                    else if (sea && math.abs(M.Dive - M.DiveTo) < 0.1f && (M.DiveWait -= dt) <= 0f)
                    {
                        // Somewhere else in the water column; now and then back up for a look about.
                        M.DiveTo = rng.NextFloat() < 0.3f ? 0f : rng.NextFloat(0.3f, 1f) * room;
                        M.DiveWait = rng.NextFloat(4f, 12f);
                    }
                }
                var speed = (M.Gasping ? Breath.RiseSpeed : Breath.DiveSpeed) * dt;
                var way = math.clamp(M.DiveTo - M.Dive, -speed, speed);
                M.Dive = math.max(0f, M.Dive + way);
                // (Pitched along the way it's going, levelling off as it gets there.)
                if (speed > 0f && math.abs(way) > 1e-5f)
                    vertical = -0.55f * way / speed * math.saturate(math.abs(M.DiveTo - M.Dive) / 0.4f + 0.3f);
            }

            // ------------------------------------------------------------------------------- fishers

            /// <summary>
            /// A fisher's spot: dry shore near its home (within its range) looking out over water a metre deep or more,
            /// the same one each time (searched outward from its home, from a bearing of its own): WorkSpot the shore,
            /// WorkFace the water a few metres out where its float goes.
            /// </summary>
            bool FindFishingSpot(ref CreatureState s)
            {
                var range = math.max(s.Leash * 0.9f, 30f);
                var start = (s.Individual % 360u) * math.PI / 180f;
                for (var d = 8f; d <= range; d += 6f)
                    for (int k = 0; k < 16; k++)
                    {
                        var a = start + k * math.PI / 8f;
                        var dir = new float2(math.sin(a), math.cos(a));
                        var p = s.Home + dir * d;
                        if (WaterDepth(p) < 1f)
                            continue;
                        // Back from the water to the bank.
                        var q = p;
                        for (int step = 0; step < 40 && !Walkable(q, Habitat.Land); step++)
                            q -= dir * 0.5f;
                        if (!Walkable(q, Habitat.Land))
                            continue;
                        s.WorkSpot = q - dir * 0.3f;
                        s.WorkFace = q + dir * 4f;
                        return true;
                    }
                return false;
            }

            /// <summary>
            /// A fisher at the water's edge (Work, stage 1 on), facing the water: waiting with its float out (1),
            /// glancing about now and then; now and then casting again (2: ActionTime through the swing); sometimes a
            /// bite and a fish landed, held up on the line a moment before it goes in the basket (3), then a fresh cast.
            /// It packs up at dusk.
            /// </summary>
            void Fishing(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float desiredLook)
            {
                if (Night > 0.5f)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                var toWater = s.WorkFace - s.Position.xz;
                var wanted = WrapAngle(math.atan2(toWater.x, toWater.y) - s.Yaw);
                s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                switch (s.Stage)
                {
                    case 2:
                        s.ActionTime = math.saturate(s.ActionTime + dt / 1.6f);
                        if (s.ActionTime >= 1f)
                        {
                            s.Stage = 1;
                            s.StageTime = 0f;
                            s.ActionTime = 0f;
                        }
                        break;
                    case 3:
                        desiredLook = 0f;
                        s.ActionTime = math.saturate(s.ActionTime + dt / 3f);
                        if (s.ActionTime >= 1f)
                        {
                            s.Stage = 2;
                            s.StageTime = 0f;
                            s.ActionTime = 0f;
                        }
                        break;
                    default:
                        s.Stage = 1;
                        desiredLook = math.frac(s.ModeTime / 11f) > 0.8f ? math.sin(s.ModeTime) * 0.8f : 0f;
                        if (rng.NextFloat() < dt / 75f)
                        {
                            s.Stage = 3;   // a bite
                            s.StageTime = 0f;
                            s.ActionTime = 0f;
                        }
                        else if (rng.NextFloat() < dt / 25f)
                        {
                            s.Stage = 2;   // reel in and cast again
                            s.StageTime = 0f;
                            s.ActionTime = 0f;
                        }
                        break;
                }
            }

            // ------------------------------------------------------------------------------- guards

            /// <summary>Whether a guard is on its shift: half by day, half by night (by its individual number), with a
            /// little gap at dawn and dusk while the watch changes.</summary>
            static bool NightShift(in CreatureState s) => ((s.Individual >> 3) & 1u) != 0;   // (bit 0 is always set)

            bool OnShift(in CreatureState s) =>
                s.Task == CreatureTask.Patrol && (NightShift(s) ? Night > 0.55f : Night < 0.45f);

            /// <summary>The settlement a resident belongs to (whose middle its home is nearest), or -1.</summary>
            int SettlementOf(float2 home)
            {
                if (!Terrain.Settlements.IsCreated)
                    return -1;
                ref var towns = ref Terrain.Settlements.Value.Settlements;
                var best = -1;
                var bestD = float.MaxValue;
                for (int i = 0; i < towns.Length; i++)
                {
                    var d = math.distance(towns[i].Centre, home);
                    if (d < towns[i].Radius * 1.5f && d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                return best;
            }

            /// <summary>Sets a guard going on its round, from the leg it's in the slice of (false if it has no town).</summary>
            bool StartPatrol(ref CreatureState s)
            {
                var town = SettlementOf(s.Home);
                if (town < 0)
                    return false;
                var legs = RoundLegs(s);
                var off = s.Position.xz - Terrain.Settlements.Value.Settlements[town].Centre;
                var leg = ((int)math.round(math.atan2(off.x, off.y) / (2f * math.PI / legs)) % legs + legs) % legs;
                if (!RoundPoint(town, leg, legs, InnerRound(s), out var point, out var gate))
                    return false;
                SetMode(ref s, CreatureMode.Patrol);
                M.RouteLeg = (byte)leg;
                s.WorkSpot = point;
                s.WorkFace = s.Position.xz;
                s.AnchorSize = gate ? 1f : 0f;
                return true;
            }

            // A guard's round, fixed by its individual number (so the same on every machine): most walk the bounds, a
            // loop just inside the town's edge an eighth of the way round at a time, by way of its gates; some an inner
            // loop round the square, a quarter of the way round at a time; one way round or the other.
            static bool InnerRound(in CreatureState s) => CreatureBlob.Trait(s.Individual, 33) > 0.4f;
            static int RoundWay(in CreatureState s) => CreatureBlob.Trait(s.Individual, 34) > 0f ? 1 : -1;
            static int RoundLegs(in CreatureState s) => InnerRound(s) ? 4 : 8;

            /// <summary>
            /// The point a leg of a round makes for: on the bounds, the town's gate in that leg's slice (seen from the
            /// middle) if it has one there (gate true), else a point just inside the edge, drawn in (or turned aside a
            /// little) until it's clear of the houses and their yards and on dry ground; round the square, a point about
            /// a quarter of the way out, likewise clear, or failing that the square's edge. False if there's nowhere
            /// clear on the bounds.
            /// </summary>
            bool RoundPoint(int settlement, int leg, int legs, bool inner, out float2 point, out bool gate)
            {
                ref var town = ref Terrain.Settlements.Value.Settlements[settlement];
                var slice = 2f * math.PI / legs;
                var angle = leg * slice;
                gate = false;
                point = town.Centre;
                if (!inner && Terrain.Anchors.IsCreated)
                {
                    ref var anchors = ref Terrain.Anchors.Value;
                    var found = new NativeList<int>(8, Allocator.Temp);
                    Anchors.Near(ref anchors, new float3(town.Centre.x, 0f, town.Centre.y), town.Radius * 1.3f, AnchorKind.Gate, ref found);
                    var best = float.MaxValue;
                    for (int i = 0; i < found.Length; i++)
                    {
                        var a = anchors.Anchors[found[i]];
                        var off = a.Position.xz - town.Centre;
                        var turn = math.abs(WrapAngle(math.atan2(off.x, off.y) - angle));
                        if (a.InCave || a.OwnerIndex != settlement || turn > slice * 0.5f || turn >= best)
                            continue;
                        best = turn;
                        point = a.Position.xz;
                        gate = true;
                    }
                    found.Dispose();
                    if (gate)
                        return true;
                }
                // (Straight out from the middle first, then a little to either side within the slice: a spoke of
                // houses can fill the line from the middle out.)
                for (int side = 0; side < 5; side++)
                {
                    var turn = (side == 0 ? 0f : (side % 2 == 1 ? 1f : -1f) * (side < 3 ? 0.2f : 0.4f)) * slice;
                    math.sincos(angle + turn, out var sin, out var cos);
                    var dir = new float2(sin, cos);
                    for (int k = 0; k < 8; k++)
                    {
                        var r = town.Radius * (inner ? 0.22f + 0.04f * k : 0.9f - 0.05f * k);
                        point = town.Centre + dir * r;
                        if (!InYard(settlement, point) && Walkable(point, Habitat.Land))
                            return true;
                    }
                }
                // Round the square, nowhere clear in the slice: the square's own edge there (open ground), so the
                // round is always a whole ring.
                if (inner)
                {
                    math.sincos(angle, out var sin, out var cos);
                    point = town.Centre + new float2(sin, cos) * town.Radius * 0.15f;
                    return true;
                }
                return false;
            }

            /// <summary>Whether a point is on a built plot of a settlement (a house and its yard, a metre round).</summary>
            bool InYard(int settlement, float2 at)
            {
                ref var set = ref Terrain.Settlements.Value;
                ref var town = ref set.Settlements[settlement];
                for (int i = town.FirstPlot; i < town.FirstPlot + town.PlotCount; i++)
                {
                    ref var plot = ref set.Plots[i];
                    if (plot.Building >= 0 && math.all(at > plot.Min - 1f) && math.all(at < plot.Max + 1f))
                        return true;
                }
                return false;
            }

            /// <summary>
            /// A beast to drive off (a wolf, a bear or the like) well inside the guard's town (10 m in from its edge: not
            /// one just roaming by) and within 25 m of it (35 m by torchlight), that fewer than two other guards are
            /// already after: the nearest.
            /// </summary>
            bool FindMenace(Entity self, in CreatureState s, out Entity menace)
            {
                menace = Entity.Null;
                var town = SettlementOf(s.Home);
                if (town < 0)
                    return false;
                ref var t = ref Terrain.Settlements.Value.Settlements[town];
                var best = s.Held == HeldItem.Torch ? 35f : 25f;
                var c = Cell(s.Position.xz);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (!Bodies[j].Blob.Value.DrivenOff || States[j].Gone)
                            continue;
                        var at = States[j].Position.xz;
                        var d = math.distance(at, s.Position.xz);
                        if (d >= best || math.distance(at, t.Centre) > t.Radius - 10f || Chasers(self, Entities[j], at) >= 2)
                            continue;
                        best = d;
                        menace = Entities[j];
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return menace != Entity.Null;
            }

            /// <summary>How many guards (other than this one) are running at the beast.</summary>
            int Chasers(Entity self, Entity beast, float2 at)
            {
                var count = 0;
                var c = Cell(at);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] != self && States[j].Mode == CreatureMode.Patrol && States[j].Stage == 2 && States[j].Partner == beast)
                            count++;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return count;
            }

            /// <summary>
            /// For a beast in a town: a guard running at it (noticed within 30 m), or a night guard's torch within 12 m;
            /// and where that guard is.
            /// </summary>
            bool FindChaser(Entity self, in CreatureState s, out float2 at)
            {
                using var profiled = ChaserMarker.Auto();
                at = default;
                var c = Cell(s.Position.xz);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (other.Mode != CreatureMode.Patrol)
                            continue;
                        var d = math.distance(other.Position.xz, s.Position.xz);
                        if (other.Stage == 2 && other.Partner == self && d < 30f || other.Held == HeldItem.Torch && d < 12f)
                        {
                            at = other.Position.xz;
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>Another guard pausing within a few metres (for a word), and where it is.</summary>
            bool FindGuard(Entity self, in CreatureState s, out float2 at)
            {
                at = default;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || States[j].Mode != CreatureMode.Patrol || States[j].Stage != 1)
                            continue;
                        if (math.distance(States[j].Position.xz, s.Position.xz) < 3.5f)
                        {
                            at = States[j].Position.xz;
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>
            /// A guard at the point its leg of the round was making for: a while there at a gate, looking out down the
            /// road, or at either of two lookouts (the first leg's point and the one halfway round), looking about; then
            /// on to the next leg's point.
            /// </summary>
            void NextOnRound(ref CreatureState s, ref Random rng)
            {
                var town = SettlementOf(s.Home);
                if (town < 0)
                    return;
                var legs = RoundLegs(s);
                var inner = InnerRound(s);
                var reached = M.RouteLeg % legs;
                var pause = s.AnchorSize > 0f ? rng.NextFloat(8f, 16f) : reached == 0 || reached == legs / 2 ? rng.NextFloat(4f, 8f) : 0f;
                var leg = reached;
                for (int k = 0; k < legs; k++)
                {
                    leg = (leg + legs + RoundWay(s)) % legs;
                    if (RoundPoint(town, leg, legs, inner, out var point, out var gate) && math.distancesq(point, s.WorkSpot) > 1f)
                    {
                        M.RouteLeg = (byte)leg;
                        s.WorkFace = s.WorkSpot;
                        s.WorkSpot = point;
                        s.AnchorSize = gate ? 1f : 0f;
                        break;
                    }
                }
                if (pause > 0f)
                {
                    s.Stage = 1;
                    s.StageTime = 0f;
                    s.Timer = pause;
                }
            }

            /// <summary>
            /// A guard on its round: to the point it's making for (stage 0), at a slow, steady pace;
            /// at a gate or a lookout it stops to look about (stage 1), longer at a gate, looking out down the road;
            /// meeting another guard stopped there, it turns to it for a word. Anyone running or carrying on close by, it
            /// stops to watch. Off shift, it goes on with its own day.
            /// </summary>
            void Patrol(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run, float3 player,
                        float playerSpeed, IdleAction emote, ref float targetSpeed, ref bool moving, ref float desiredLook)
            {
                if (!OnShift(s))
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                // Its weapon: worn in its sheath on its rounds; drawn when there's something to see to (a beast to drive
                // off, someone running or carrying on close by), and put away again once all's been quiet a few
                // seconds. Drawing or sheathing is a half-second reach (Draw, Sheathe: the action time every machine
                // sees), the weapon passing from sheath to hand at the middle of it. (By night a torch fills its hand;
                // an elf's bow stays on its back.)
                if (Night <= 0.5f && b.GuardKit != GuardKit.Elf && b.Hands)
                {
                    var near = player.xz - s.Position.xz;
                    var alert = s.Stage == 2 || math.length(near) < 15f && (playerSpeed > 7f || emote != IdleAction.None);
                    M.WeaponCalm = alert ? 0f : M.WeaponCalm + dt;
                    if (s.Action == IdleAction.None && alert && s.Held == HeldItem.Spear)
                    {
                        s.Held = HeldItem.SpearDrawn;
                        s.Action = IdleAction.Draw;
                        s.ActionTime = 0f;
                        s.ActionLength = 0.5f;
                    }
                    else if (s.Action == IdleAction.None && !alert && s.Held == HeldItem.SpearDrawn && M.WeaponCalm > 4f)
                    {
                        s.Held = HeldItem.Spear;
                        s.Action = IdleAction.Sheathe;
                        s.ActionTime = 0f;
                        s.ActionLength = 0.5f;
                    }
                }
                // A wolf, a bear or the like come into the town: the guard runs at it, spear levelled (stage 2), till it's
                // off out past the town's edge, then back to its round (to the point it was making for). It never goes
                // more than 30 m beyond the edge after one, nor for more than 40 s, and leaves any one beast to a pair;
                // after one, a minute and a half before it goes after another (so it's now and then, not all day).
                M.ChaseCooldown = math.max(M.ChaseCooldown - dt, 0f);
                if (s.Stage == 2)
                {
                    var town = SettlementOf(s.Home);
                    if (town >= 0 && s.StageTime < 40f && Lookup.TryGetValue(s.Partner, out var p) && !States[p].Gone)
                    {
                        ref var t = ref Terrain.Settlements.Value.Settlements[town];
                        var beast = States[p].Position.xz;
                        if (math.distance(beast, t.Centre) < t.Radius + 8f && math.distance(s.Position.xz, t.Centre) < t.Radius + 30f)
                        {
                            s.Target = beast;
                            // (Not right up to it: a spear's length off, it slows to keep there.)
                            targetSpeed = math.distance(s.Position.xz, beast) > 2.5f ? run : 0f;
                            moving = true;
                            desiredLook = 0f;
                            return;
                        }
                    }
                    s.Stage = 0;
                    s.StageTime = 0f;
                    s.Partner = Entity.Null;
                    M.ChaseCooldown = 90f;
                }
                else if (M.ChaseCooldown <= 0f && FindMenace(self, in s, out var menace))
                {
                    s.Stage = 2;
                    s.StageTime = 0f;
                    s.Partner = menace;
                    return;
                }
                var toPlayer = player.xz - s.Position.xz;
                var watching = math.length(toPlayer) < 15f && (playerSpeed > 7f || emote != IdleAction.None);
                if (s.Stage == 0)
                {
                    s.Target = s.WorkSpot;
                    moving = !watching;
                    targetSpeed = watching ? 0f : walk * 0.7f;
                    if (math.distance(s.Position.xz, s.WorkSpot) < 0.7f)
                        NextOnRound(ref s, ref rng);
                }
                else
                {
                    s.Timer -= dt;
                    desiredLook = math.sin(s.StageTime * 0.9f + s.Individual % 7) * 1.1f;
                    if (FindGuard(self, s, out var other))
                    {
                        // A word with the other guard (for a while, then on).
                        var to = other - s.Position.xz;
                        var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                        desiredLook = 0f;
                        if (s.StageTime < 10f)
                            s.Timer = math.max(s.Timer, 0.5f);
                    }
                    if (s.Timer <= 0f)
                    {
                        s.Stage = 0;
                        s.StageTime = 0f;
                    }
                }
                if (watching)
                {
                    var wanted = WrapAngle(math.atan2(toPlayer.x, toPlayer.y) - s.Yaw);
                    if (s.Stage == 0)
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                    desiredLook = math.clamp(WrapAngle(math.atan2(toPlayer.x, toPlayer.y) - s.Yaw), -1.2f, 1.2f);
                }
            }

            /// <summary>
            /// A door to go in by: one of the three Door anchors of its settlement nearest where it is (which of them,
            /// by its individual number, so neighbours spread over the houses round them). False if there are none in
            /// its range.
            /// </summary>
            bool FindDoor(ref CreatureState s)
            {
                // (A-103: its own door first, when it has one no anchor marks: a turtle folk's igloo tunnel.)
                if (math.lengthsq(s.OwnDoor.xz) > 0f)
                {
                    s.Anchor = new float3(s.OwnDoor.x, Terrain.SampleHeight(s.OwnDoor.xz), s.OwnDoor.z);
                    s.AnchorYaw = s.OwnDoor.w;
                    return true;
                }
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var doors = new NativeList<int>(16, Allocator.Temp);
                Anchors.Near(ref set, s.Position, 30f, AnchorKind.Door, ref doors);
                if (doors.Length == 0)
                    Anchors.Near(ref set, new float3(s.Home.x, s.Position.y, s.Home.y), s.Leash + 10f, AnchorKind.Door, ref doors);
                if (doors.Length == 0)
                {
                    doors.Dispose();
                    return false;
                }
                // The nearest three, nearest first (a few insertion passes: the list is short).
                var nearest = new FixedList32Bytes<int>();
                for (int k = 0; k < doors.Length; k++)
                {
                    var d = math.distancesq(set.Anchors[doors[k]].Position.xz, s.Position.xz);
                    var at = nearest.Length;
                    while (at > 0 && math.distancesq(set.Anchors[nearest[at - 1]].Position.xz, s.Position.xz) > d)
                        at--;
                    if (at < 3)
                    {
                        nearest.Insert(at, doors[k]);
                        if (nearest.Length > 3)
                            nearest.RemoveAt(3);
                    }
                }
                doors.Dispose();
                var door = set.Anchors[nearest[(int)(s.Individual % (uint)nearest.Length)]];
                s.Anchor = door.Position;
                s.AnchorYaw = math.atan2(door.Facing.x, door.Facing.y);
                return true;
            }

            /// <summary>
            /// Goes indoors: walks to its door, steps in (fading from sight), stays in (through the night, or a while by
            /// day), then steps out again and gets on with its day. A door it can't get to, it gives up on.
            /// </summary>
            void GoIndoors(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, ref float targetSpeed, ref bool moving,
                           ref bool custom)
            {
                var door = s.Anchor.xz;
                var outward = new float2(math.sin(s.AnchorYaw), math.cos(s.AnchorYaw));
                const float through = 0.9f;   // seconds to step through
                switch (s.Stage)
                {
                    case 0:
                        s.Target = door;
                        targetSpeed = walk;
                        moving = true;
                        // (A-105: the walk home to its own house can be a long one, in from the fields, a pier or the mine.)
                        if (math.distance(s.Position.xz, door) < 0.4f)
                            NextStage(ref s);
                        else if (s.ModeTime > (HasOwnDoor(s) ? 240f : 60f))
                            Pause(ref s, ref b, ref rng);
                        break;
                    case 1:
                        custom = true;
                        s.Yaw = WrapAngle(s.AnchorYaw + math.PI);
                        s.Speed = 0.6f;
                        s.Position.xz = door - outward * (0.7f * math.saturate(s.StageTime / through));
                        s.Position.y = Terrain.SampleHeight(s.Position.xz);
                        s.ActionTime = math.saturate(s.StageTime / through);
                        CreatureMotion.AdvancePhase(ref s, ref b, dt);
                        if (s.StageTime >= through)
                            NextStage(ref s);
                        break;
                    case 2:
                        custom = true;
                        s.Speed = 0f;
                        s.ActionTime = 1f;
                        if (!Homeward(s, ref b))
                            s.Timer -= dt;
                        // (A guard comes out when its watch begins, however long it meant to stay in: a night guard
                        // asleep through the day is out again at dusk.)
                        if (s.Timer <= 0f || OnShift(s))
                            NextStage(ref s);
                        break;
                    default:
                        custom = true;
                        s.Yaw = s.AnchorYaw;
                        s.Speed = 0.6f;
                        s.Position.xz = door - outward * (0.7f * (1f - math.saturate(s.StageTime / through)));
                        s.Position.y = Terrain.SampleHeight(s.Position.xz);
                        s.ActionTime = 1f - math.saturate(s.StageTime / through);
                        CreatureMotion.AdvancePhase(ref s, ref b, dt);
                        if (s.StageTime >= through)
                            Pause(ref s, ref b, ref rng);
                        break;
                }
            }

            // ------------------------------------------------------------------------------- people

            /// <summary>
            /// Game for a hunter: the nearest wild grazer or small animal on land (not a predator, not a settlement's own,
            /// not someone's body) within range and within the hunter's own range from home.
            /// </summary>
            bool FindGame(Entity self, ref CreatureState s, float range, out Entity found)
            {
                found = Entity.Null;
                var best = range;
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(range / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        ref var ob = ref Bodies[j].Blob.Value;
                        var size = ob.BodyLength * other.Scale;
                        if (Entities[j] == self || ob.Upright || ob.Habitat != Habitat.Land || ob.Prey.Length > 0 || ob.CaveDweller ||
                            other.Leash > 0f || other.Gone || other.Mode == CreatureMode.Puppet || other.Mode == CreatureMode.Held ||
                            size < 0.25f || size > 2.6f || ob.FleeDistance <= 0f ||
                            s.Leash > 0f && math.distance(other.Position.xz, s.Home) > s.Leash)
                            continue;
                        var d = math.distance(other.Position.xz, s.Position.xz);
                        if (d < best)
                        {
                            best = d;
                            found = Entities[j];
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found != Entity.Null;
            }

            /// <summary>
            /// A hunter's outing (stages in FolkHunting): out towards the game openly, then creeping bent low once
            /// within 25 m; at about 11 m it stops and draws back (Aim), throws (Throw), and runs after the game a
            /// little way as it bolts (Chase), before giving up and heading home. Nothing is killed (no hunt ends in a
            /// kill until there's a health system: the throw always misses). Gives up if the game is gone or too long passes.
            /// </summary>
            void FolkHunt(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run,
                          ref float targetSpeed, ref bool moving, ref float desiredLook, ref float bow, ref float headDown)
            {
                s.Timer -= dt;
                if (s.Timer <= 0f || !Lookup.TryGetValue(s.Partner, out var p) || States[p].Gone)
                {
                    HeadHome(ref s, ref rng);
                    return;
                }
                var prey = States[p];
                var to = prey.Position.xz - s.Position.xz;
                var distance = math.length(to);
                switch (s.Stage)
                {
                    case FolkHunting.Stalk:
                    {
                        var close = distance < 25f;
                        s.Target = prey.Position.xz - math.normalizesafe(to) * 1.5f;
                        moving = true;
                        targetSpeed = close ? walk * 0.35f : walk;
                        if (close)
                        {
                            bow = 1f;
                            headDown = 0.2f;
                        }
                        // It's seen them coming: off it goes, and the hunter after it.
                        if (prey.Mode == CreatureMode.Flee && close)
                            NextStage(ref s, FolkHunting.Chase);
                        else if (distance < 11f)
                            NextStage(ref s, FolkHunting.Aim);
                        break;
                    }
                    case FolkHunting.Aim:
                        TurnTowards(ref s, ref b, prey.Position.xz, dt);
                        bow = 0.3f;
                        if (s.StageTime > 1.4f)
                            NextStage(ref s, FolkHunting.Throw);
                        break;
                    case FolkHunting.Throw:
                        TurnTowards(ref s, ref b, prey.Position.xz, dt);
                        bow = 1f;   // leaning into the throw
                        if (s.StageTime > 0.7f)
                            NextStage(ref s, FolkHunting.Chase);
                        break;
                    default:
                        // After it a little way, then gives up.
                        s.Target = prey.Position.xz;
                        moving = true;
                        targetSpeed = run * 0.6f;
                        if (s.StageTime > 4f || distance > 40f)
                            HeadHome(ref s, ref rng);
                        break;
                }
                desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1f, 1f);
            }

            static void NextStage(ref CreatureState s, byte stage)
            {
                s.Stage = stage;
                s.StageTime = 0f;
            }

            /// <summary>Back towards home (a settlement's folk), to take up the day there.</summary>
            static void HeadHome(ref CreatureState s, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Wander);
                s.Target = (s.Leash > 0f ? s.Home : s.Position.xz) + rng.NextFloat2Direction() * rng.NextFloat(2f, 8f);
            }

            /// <summary>
            /// A hunter after this animal near enough to notice: creeping within 5 m (heard), aiming within 15 m (seen), or
            /// throwing or running after it within 40 m. Where they are.
            /// </summary>
            bool FindHunter(Entity self, ref CreatureState s, float alert, out float2 at)
            {
                using var profiled = HunterMarker.Auto();
                // (Only the hunters out now are looked at: usually none, so it costs nothing.)
                at = default;
                for (int h = 0; h < Hunters.Length; h++)
                {
                    var other = States[Hunters[h]];
                    if (other.Partner != self)
                        continue;
                    var range = other.Stage == FolkHunting.Stalk ? 5f * alert : other.Stage == FolkHunting.Aim ? 15f * alert : 40f;
                    if (math.distance(other.Position.xz, s.Position.xz) < range)
                    {
                        at = other.Position.xz;
                        return true;
                    }
                }
                return false;
            }

            /// <summary>Where the player with this Holder id is, and which way they face (false if they've gone).</summary>
            bool FindHolder(int holder, out float3 position, out float yaw) => FindHolder(holder, out position, out yaw, out _);

            bool FindHolder(int holder, out float3 position, out float yaw, out int species)
            {
                for (int p = 0; p < PlayerIds.Length; p++)
                    if (PlayerIds[p] == holder)
                    {
                        position = Players[p];
                        yaw = PlayerYaws[p];
                        species = PlayerSpecies[p];
                        return true;
                    }
                position = default;
                yaw = 0f;
                species = -1;
                return false;
            }
        }
    }
}
