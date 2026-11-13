using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    public partial class DebugControls
    {
        /// <summary>"-ridelife" (A-104 row 2): rides a town makes by itself, framed for shots, then each way of getting down.</summary>
        bool rideLife;
        bool rideLifeDone;
        /// <summary>When the town part began, and when the dismount part began (Time.time; -1 before).</summary>
        float rideLifeAt = -1f, rideOffAt = -1f;
        /// <summary>When the test warped to the nearest settlement (Time.time; -1 before).</summary>
        float rideWarpAt = -1f;
        /// <summary>How long the town part runs (s), and how long each dismount case waits for its rider to get down.</summary>
        const float RideLifeTime = 150f, RideOffWait = 4f;
        /// <summary>Kinds already framed for a shot, and a pair held still for one (mount, its state before; till when).</summary>
        readonly HashSet<RideKind> rideShot = new HashSet<RideKind>();
        /// <summary>When each rider was first seen riding (Time.time).</summary>
        readonly Dictionary<Entity, float> rideSeenAt = new Dictionary<Entity, float>();
        /// <summary>When the test moved the player into the village's middle (Time.time; -1 before).</summary>
        float rideMovedAt = -1f;
        Entity rideHeld;
        CreatureState rideHeldWas;
        float rideHeldUntil;
        /// <summary>The dismount cases: what's done to the mount, the reason expected, the pair, and when it was set going.</summary>
        int rideCase = -1;
        float rideCaseAt;
        Entity rideCaseMount, rideCaseRider;
        int rideCaseEnded;
        /// <summary>The case's rider (CreatureState.Individual), to find its own line in the ride log.</summary>
        uint rideCaseRiderId;
        /// <summary>When the player was moved beside the wade case's water (Time.time; -1 before), and the spot.</summary>
        float rideWadeMovedAt = -1f;
        float3 rideWadeSpot;
        float3 rideCaseSpot;
        readonly List<string> rideCaseLines = new List<string>();
        bool rideCasePass = true;
        static readonly (string name, RideOff expect, bool child)[] RideCases =
        {
            ("sleep", RideOff.Sleep, false), ("hunt", RideOff.Fight, false), ("flee", RideOff.Fight, true), ("climb", RideOff.Climb, false),
            ("swim", RideOff.Swim, false), ("picked up", RideOff.Held, false), ("killed", RideOff.Death, true),
            ("removed", RideOff.Death, false),
            // (Last: the player is moved to the water for it.)
            ("wade", RideOff.Swim, false),
        };

        /// <summary>
        /// "-ridelife" (with "-warp town" or the like): solo or hosting, once the world has settled. First RideLifeTime of
        /// the town as it is, with every ride roll passing (RideLifeSystem.Force 3) so rides come often enough to see:
        /// every 10 s the rides going on (who on whom, where); the first child's ride home and the first pet's ride are
        /// each held still for 8 s and a -camlook line logged for its shot; each ride's end is logged as it happens (a
        /// child's near the door, a pet's when its time is up). Then no more rides begin (Force 4) and each dismount case
        /// in turn puts a pair (a cat on a dog, or a child on a grown-up) down near the town's middle, does that to the
        /// mount and logs where the rider got down: its side, how far clear of the mount's body (Room), how far over the
        /// floor (Floor), and whether it landed in a building's walls (the brain may swap a mode set from outside for its
        /// own before RideSystem sees it, so the reason each mode gives is checked straight too, ReasonTable). PASS when
        /// every row of that table is right and each case got down clear of the mount (Room above 0, or where it was for
        /// a mount taken out of the world), on the floor and out of walls.
        /// </summary>
        void RideLifeTest()
        {
            if (!rideLife || rideLifeDone || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return;
            if (!director.CanSpawn)
            {
                Debug.LogWarning("RideLife: needs a solo or hosting game");
                rideLifeDone = true;
                return;
            }
            var em = director.SimWorld.EntityManager;
            ref var log = ref RideLog.Data.Data;
            // "-warp town" on the command line can run against the first, stale bake (before "-island" re-bakes the
            // world, so the spot can be empty country): warp to the nearest settlement again now the world has settled,
            // then on to the nearest village (camps and holds have no children, and their animals are mostly one kind),
            // and give its folk 10 s to spawn around the player.
            if (rideWarpAt < 0f)
            {
                rideWarpAt = Time.time;
                warpIndex[1] = -1;
                Warp(1, 1);
                var towns = warpTargets[1];
                for (int i = 1; i < towns.Count; i++)
                    if (towns[0].Name.Contains("Village"))
                        break;
                    else if (towns[i].Name.Contains("Village"))
                    {
                        Warp(1, i);
                        break;
                    }
                return;
            }
            if (Time.time - rideWarpAt < 10f)
                return;
            // The warp spot is on the road outside: once the folk are out, in among them (the middle of the village's
            // children, where they play and set off home from), and 10 s more for the animals there to come out.
            if (rideMovedAt < 0f)
            {
                rideMovedAt = Time.time;
                if (VillageMiddle(em, player.Position, out var middle))
                {
                    MovePlayer(source, middle);
                    Debug.Log($"RideLife: moved into the village's middle at ({middle.x:F0}, {middle.y:F0})");
                }
                return;
            }
            if (Time.time - rideMovedAt < 10f)
                return;
            if (rideLifeAt < 0f)
            {
                rideLifeAt = Time.time;
                log.On = 1;
                log.Begun = log.Ended = 0;
                log.Offs.Clear();
                RideLifeSystem.Force.Data = 3;
                Debug.Log($"RideLife: town part, {RideLifeTime:F0} s at ({player.Position.x:F0}, {player.Position.z:F0}), every roll passing");
            }
            var since = Time.time - rideLifeAt;
            var tick = Mathf.FloorToInt(since / 10f) != Mathf.FloorToInt((since - Time.deltaTime) / 10f);
            var floraQuery = em.CreateEntityQuery(ComponentType.ReadOnly<FloraLookup>());
            var flora = floraQuery.TryGetSingleton<FloraLookup>(out var fl) ? fl.Blob : default;

            // Rides that ended since last frame (the town part's: the cases log their own).
            while (rideCaseEnded < log.Ended && log.Offs.Length > 0 && rideCase < 0)
            {
                var n = log.Offs.Length - (log.Ended - rideCaseEnded);
                if (n >= 0)
                    Debug.Log($"RideLife: {Describe(log.Offs[n], source, flora)}");
                rideCaseEnded++;
            }

            if (since < RideLifeTime)
            {
                // A pair held for its shot: let it go again after 8 s, as it was (its walk home, its wandering).
                if (rideHeld != Entity.Null && Time.time > rideHeldUntil)
                {
                    if (em.Exists(rideHeld))
                    {
                        var m = em.GetComponentData<CreatureState>(rideHeld);
                        m.Mode = rideHeldWas.Mode;
                        m.Stage = rideHeldWas.Stage;
                        m.Timer = rideHeldWas.Timer;
                        em.SetComponentData(rideHeld, m);
                    }
                    rideHeld = Entity.Null;
                }
                if (!tick && rideHeld != Entity.Null)
                    return;
                using var query = em.CreateEntityQuery(ComponentType.ReadOnly<Fauna.Rider>(), ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                using var riders = query.ToEntityArray(Allocator.Temp);
                var line = tick ? new System.Text.StringBuilder($"RideLife {since:F0} s: {log.Begun} begun, {log.Ended} ended, {riders.Length} riding:") : null;
                if (line != null)
                    line.Append($" [near: {TownCensus(em, player.Position)}]");
                foreach (var e in riders)
                {
                    var ride = em.GetComponentData<Fauna.Rider>(e);
                    if (!em.Exists(ride.Mount))
                        continue;
                    var r = em.GetComponentData<CreatureState>(e);
                    var m = em.GetComponentData<CreatureState>(ride.Mount);
                    var rName = director.SpeciesName(em.GetComponentData<CreatureBody>(e).Species);
                    var mName = director.SpeciesName(em.GetComponentData<CreatureBody>(ride.Mount).Species);
                    line?.Append($" {rName} on {mName} ({ride.Kind}, {(RidePose)r.Stage}, mount {m.Mode} at ({m.Position.x:F1}, {m.Position.y:F2}, {m.Position.z:F1})" +
                                 (ride.Left > 0f ? $", {ride.Left:F0} s left" : "") + ");");
                    // The first of each kind, once it's been going 3 s: held still for 8 s and framed from its right.
                    // (A rider's ModeTime stands still while it rides, the brain passing it by: timed from when it's seen.)
                    if (!rideSeenAt.TryGetValue(e, out var seen))
                        rideSeenAt[e] = seen = Time.time;
                    if (rideHeld == Entity.Null && Time.time - seen > 3f && !rideShot.Contains(ride.Kind) && ride.Kind != RideKind.Placed)
                    {
                        rideShot.Add(ride.Kind);
                        rideHeld = ride.Mount;
                        rideHeldWas = m;
                        rideHeldUntil = Time.time + 8f;
                        m.Mode = CreatureMode.Puppet;
                        m.Speed = m.GaitSpeed = 0f;
                        em.SetComponentData(ride.Mount, m);
                        var child = ride.Kind == RideKind.WalkHome;
                        var target = r.Position + new float3(0f, child ? 0.1f : 0.15f, 0f);
                        var eye = ShotEye(source, flora, target, m.Yaw, child ? 1.5f : 0.9f);
                        Debug.Log($"RideLife: shot {ride.Kind} {rName} on {mName}: -camlook {eye.x:F2},{eye.y:F2},{eye.z:F2},{target.x:F2},{target.y:F2},{target.z:F2}");
                    }
                }
                if (line != null)
                    Debug.Log(line.ToString());
                return;
            }

            // The dismount cases, one at a time.
            if (rideOffAt < 0f)
            {
                rideOffAt = Time.time;
                RideLifeSystem.Force.Data = 4;
                Debug.Log($"RideLife: town part done: {log.Begun} rides begun, {log.Ended} ended; kinds framed: {string.Join(", ", rideShot)}");
                rideCaseSpot = FindCaseSpot(source, flora, player.Position);
                // (A second NaN probe over the cases: the first covered the town part's start.)
                SparkleProbe.Begin("ridecases", cap: 16f, cropDir: shotDir);
                rideCase = 0;
                rideCaseAt = -1f;
            }
            if (rideCase >= RideCases.Length)
                return;
            var c = RideCases[rideCase];
            if (rideCaseAt < 0f)
            {
                var spot = rideCaseSpot + new float3(3f * (rideCase % 3) - 3f, 0f, 3f * (rideCase / 3) - 3f);
                if (c.name == "wade")
                {
                    if (rideWadeMovedAt < 0f)
                    {
                        if (!FindWade(source, player.Position, out rideWadeSpot))
                        {
                            rideCaseLines.Add("wade: no shallow water within 600 m (skipped)");
                            rideCase++;
                            return;
                        }
                        // (Beside it: FaunaDirector lets go of anything 170 m from every player, the pair with it; and
                        // a host's player is where its client last said, so a few seconds for that to catch up.)
                        MovePlayer(source, rideWadeSpot.xz + new float2(4f, 0f));
                        rideWadeMovedAt = Time.time;
                        return;
                    }
                    if (Time.time - rideWadeMovedAt < 4f)
                        return;
                    spot = rideWadeSpot;
                }
                spot.y = source.FloorUnder(spot + new float3(0f, 1f, 0f), out _);
                var mountIndex = director.SpeciesIndex(c.child ? "Human" : "Dog");
                var riderIndex = director.SpeciesIndex(c.child ? "HumanChild" : "Cat");
                rideCaseMount = director.Spawn(mountIndex, spot, 0.7f * rideCase);
                rideCaseRider = director.Spawn(riderIndex, spot + new float3(1.2f, 0f, 0f), 0f);
                foreach (var e in new[] { rideCaseMount, rideCaseRider })
                {
                    var s = em.GetComponentData<CreatureState>(e);
                    s.Mode = CreatureMode.Puppet;
                    s.Speed = s.GaitSpeed = 0f;
                    em.SetComponentData(e, s);
                }
                var refusal = Riding.Mount(em, rideCaseRider, rideCaseMount, source);
                if (refusal != RideRefusal.None)
                {
                    rideCaseLines.Add($"{c.name}: REFUSED ({refusal})");
                    rideCasePass = false;
                    rideCase++;
                    return;
                }
                if (rideCase == 0)
                    ReasonTable(em, source, player.Position);
                rideCaseAt = Time.time;
                rideCaseEnded = log.Ended;
                rideCaseRiderId = em.GetComponentData<CreatureState>(rideCaseRider).Individual;
                return;
            }
            var t = Time.time - rideCaseAt;
            // (The case's own pair's line: a ride the town began before the cases may end meanwhile.)
            var k = log.Offs.Length - 1;
            while (k >= 0 && (log.Offs[k].Kind != RideKind.Placed || log.Offs[k].Rider != rideCaseRiderId))
                k--;
            // (A second on the mount first, then the case: set again each frame till the rider is down, since the brain
            // may change its mind about a mode it didn't pick.)
            if (t > 1f && k < 0 && em.Exists(rideCaseMount))
            {
                var m = em.GetComponentData<CreatureState>(rideCaseMount);
                switch (c.name)
                {
                    case "sleep": m.Mode = CreatureMode.Sleep; m.Timer = 30f; break;
                    case "hunt": m.Mode = CreatureMode.Hunt; m.Timer = 30f; break;
                    case "flee": m.Mode = CreatureMode.Flee; m.Timer = 30f; break;
                    case "climb": m.Mode = CreatureMode.Climb; m.Timer = 30f; break;
                    case "swim": m.Mode = CreatureMode.Swim; m.Timer = 30f; break;
                    case "wade": break;   // (Stood in the water: RideSystem sees how deep.)
                    case "picked up": m.Mode = CreatureMode.Held; m.Holder = PlayerHolder(em); m.Timer = 30f; break;
                    case "killed": m.Gone = true; break;
                    case "removed": em.DestroyEntity(rideCaseMount); break;
                }
                if (c.name != "removed")
                    em.SetComponentData(rideCaseMount, m);
            }
            if (k >= 0 || t > 1f + RideOffWait)
            {
                string verdict;
                var ok = false;
                if (k >= 0)
                {
                    var off = log.Offs[k];
                    var walls = flora.IsCreated && FloraPlacement.Blocked(ref flora.Value, source, off.At.xz, 0.1f);
                    // (Down clear and on the floor is the gate here; the reason is the brain's to keep or change, since a
                    // mode set from outside may be changed by the brain before RideSystem sees it: ReasonTable checks each.)
                    // (A killed mount may be let go by FaunaDirector before RideSystem sees it dead: then, as when removed,
                    // the rider gets down where it is.)
                    ok = off.Why != RideOff.None && math.abs(off.Floor) < 0.02f && !walls && (off.Side < 0 ? c.name == "removed" || c.name == "killed" : off.Room > 0f);
                    verdict = $"{c.name}: {Describe(off, source, flora)} ({(ok ? "ok" : "WRONG")}" +
                              (off.Why != c.expect ? $"; brain changed the mode first, {c.expect} expected" : "") + ")";
                }
                else
                {
                    verdict = $"{c.name}: rider NOT down after {RideOffWait:F0} s (WRONG)";
                    if (c.name == "wade" && em.Exists(rideCaseMount))
                    {
                        var m = em.GetComponentData<CreatureState>(rideCaseMount);
                        var mb = em.GetComponentData<CreatureBody>(rideCaseMount);
                        var g = source.SampleGround(m.Position.xz);
                        verdict += $"; water {(g.HasWater ? g.WaterSurface - g.Height : 0f):F2} m deep at ({m.Position.x:F1}, {m.Position.z:F1}), " +
                                   $"belly {0.45f * mb.Blob.Value.StandHeight * m.Scale:F2} m, mode {m.Mode}";
                    }
                }
                rideCaseLines.Add(verdict);
                Debug.Log($"RideLife: case {verdict}");
                rideCasePass &= ok;
                // (Both out of the way before the next case.)
                foreach (var e in new[] { rideCaseMount, rideCaseRider })
                    if (em.Exists(e))
                        em.DestroyEntity(e);
                rideCase++;
                rideCaseAt = -1f;
                if (rideCase >= RideCases.Length)
                {
                    rideLifeDone = true;
                    log.On = 0;
                    RideLifeSystem.Force.Data = 0;
                    Debug.Log($"RideLife: {(rideCasePass ? "PASS" : "FAIL")}: {string.Join("; ", rideCaseLines)}");
                }
            }
        }

        /// <summary>
        /// Riding.Reason for each state a mount can be in, on the first case's pair (a cat on a dog): the mode-to-reason
        /// table checked straight, since a mode set from outside can be changed by the brain before RideSystem sees it.
        /// </summary>
        void ReasonTable(EntityManager em, in Punchy.Terrain.TerrainSource source, float3 near)
        {
            ref var mb = ref em.GetComponentData<CreatureBody>(rideCaseMount).Blob.Value;
            var m0 = em.GetComponentData<CreatureState>(rideCaseMount);
            var placed = new Fauna.Rider { Mount = rideCaseMount, Kind = RideKind.Placed };
            var home = new Fauna.Rider { Mount = rideCaseMount, Kind = RideKind.WalkHome };
            var rows = new List<(string name, CreatureState m, Fauna.Rider ride, RideOff expect)>();
            CreatureState With(CreatureMode mode) { var m = m0; m.Mode = mode; return m; }
            foreach (var (mode, expect) in new[] { (CreatureMode.Puppet, RideOff.None), (CreatureMode.Idle, RideOff.None), (CreatureMode.Wander, RideOff.None),
                         (CreatureMode.Sleep, RideOff.Sleep), (CreatureMode.Rest, RideOff.Sleep), (CreatureMode.Hunt, RideOff.Fight), (CreatureMode.Flee, RideOff.Fight),
                         (CreatureMode.Play, RideOff.Fight), (CreatureMode.Climb, RideOff.Climb), (CreatureMode.Roost, RideOff.Climb), (CreatureMode.Swim, RideOff.Swim),
                         (CreatureMode.Held, RideOff.Held) })
                rows.Add((mode.ToString(), With(mode), placed, expect));
            var gone = m0;
            gone.Gone = true;
            rows.Add(("Gone", gone, placed, RideOff.Death));
            var flying = With(CreatureMode.Wander);
            flying.Fly = 1f;
            rows.Add(("flying", flying, placed, RideOff.Climb));
            if (FindWade(source, near, out var wet))
            {
                var wading = With(CreatureMode.Wander);
                wading.Position = wet;
                rows.Add(($"wading {source.SampleGround(wet.xz).WaterSurface - wet.y:F2} m", wading, placed, RideOff.Swim));
            }
            var walking = With(CreatureMode.Home);
            walking.Anchor = walking.Position + new float3(10f, 0f, 0f);
            rows.Add(("home, 10 m from the door (child)", walking, home, RideOff.None));
            var there = walking;
            there.Anchor = there.Position + new float3(2f, 0f, 0f);
            rows.Add(("home, 2 m from the door (child)", there, home, RideOff.Home));
            rows.Add(("turned aside to wander (child)", With(CreatureMode.Wander), home, RideOff.Home));
            var bad = 0;
            var line = new System.Text.StringBuilder("RideLife: reasons:");
            foreach (var row in rows)
            {
                var got = Riding.Reason(ref mb, row.m, row.ride, source);
                bad += got == row.expect ? 0 : 1;
                line.Append($" {row.name} {got}{(got == row.expect ? "" : $" (WRONG, {row.expect} expected)")};");
            }
            Debug.Log(line.ToString());
            rideCaseLines.Add($"reason table {rows.Count - bad}/{rows.Count}");
            rideCasePass &= bad == 0;
        }

        /// <summary>One ride's end, as a log line: why, who, where it landed and its mount stood, the side, room and floor.</summary>
        static string Describe(in RideLog.Off off, in Punchy.Terrain.TerrainSource source, BlobAssetReference<FloraBlob> flora)
        {
            if (off.Why == RideOff.None)
                return $"{off.Kind} ride taken off by something else (rider {off.Rider:X8} now {(CreatureMode)off.Side} for {off.Room:F2} s, holder {off.Floor:F0}) at ({off.At.x:F2}, {off.At.y:F2}, {off.At.z:F2})";
            var walls = flora.IsCreated && FloraPlacement.Blocked(ref flora.Value, source, off.At.xz, 0.1f);
            var g = source.SampleGround(off.At.xz);
            var wet = g.HasWater && g.WaterSurface > off.At.y + 0.05f ? $", in {g.WaterSurface - off.At.y:F2} m of water" : "";
            return $"{off.Kind} ride off ({off.Why}): rider {off.Rider:X8} down at ({off.At.x:F2}, {off.At.y:F2}, {off.At.z:F2}), " +
                   $"mount at ({off.Mount.x:F2}, {off.Mount.y:F2}, {off.Mount.z:F2}), side {off.Side}, room {off.Room:F2} m, floor {off.Floor:F3} m" +
                   $"{wet}{(walls ? ", IN WALLS" : "")}";
        }

        /// <summary>Where the dismount cases go: the dry, open spot nearest the player with a building's walls within 4 m
        /// (so getting down clear of them is tried), else the dry spot nearest the player.</summary>
        static float3 FindCaseSpot(in Punchy.Terrain.TerrainSource source, BlobAssetReference<FloraBlob> flora, float3 near)
        {
            var fallback = near;
            var haveFallback = false;
            for (var r = 3f; r <= 60f; r += 1.5f)
                for (int k = 0; k < 24; k++)
                {
                    var a = k * math.PI * 2f / 24f;
                    var xz = near.xz + new float2(math.sin(a), math.cos(a)) * r;
                    var g = source.SampleGround(xz);
                    if (g.HasWater && g.WaterSurface > g.Height || flora.IsCreated && FloraPlacement.Blocked(ref flora.Value, source, xz, 1.5f))
                        continue;
                    var at = new float3(xz.x, g.Height, xz.y);
                    if (!haveFallback)
                    {
                        fallback = at;
                        haveFallback = true;
                    }
                    if (flora.IsCreated && FloraPlacement.Blocked(ref flora.Value, source, xz, 6f))
                        return at;
                }
            return fallback;
        }

        /// <summary>The holder id of the first player there (a host's or client's body, else solo's), so the brain keeps a
        /// test's "picked up" mount in that player's hands instead of putting it down at once for want of a holder.</summary>
        static int PlayerHolder(EntityManager em)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Net.NetPlayer>(), ComponentType.ReadOnly<Unity.NetCode.GhostOwner>());
            using var owners = q.ToComponentDataArray<Unity.NetCode.GhostOwner>(Allocator.Temp);
            return Interactions.HolderId(owners.Length > 0 ? owners[0].NetworkId : 0);
        }

        /// <summary>Who's within 60 m who could take part in a ride: village grown-ups (and how many are setting off
        /// home, Home stage 0), village children, and small tame animals (and how many are idle).</summary>
        /// <summary>The middle of the village children within 300 m (their mean place), if there are any.</summary>
        static bool VillageMiddle(EntityManager em, float3 at, out float2 middle)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var sum = float2.zero;
            var n = 0;
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                ref var b = ref bodies[i].Blob.Value;
                if (s.Gone || !b.Child || !b.Hands || s.Leash <= 0f || math.distancesq(s.Position.xz, at.xz) > 300f * 300f)
                    continue;
                sum += s.Position.xz;
                n++;
            }
            middle = n > 0 ? sum / n : at.xz;
            return n > 0;
        }

        static string TownCensus(EntityManager em, float3 at)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            int grown = 0, homeward = 0, children = 0, pets = 0, idlePets = 0;
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                if (s.Gone || math.distancesq(s.Position.xz, at.xz) > 60f * 60f)
                    continue;
                ref var b = ref bodies[i].Blob.Value;
                if (b.Hands && b.Upright && s.Leash > 0f)
                {
                    if (b.Child)
                        children++;
                    else
                    {
                        grown++;
                        homeward += s.Mode == CreatureMode.Home && s.Stage == 0 ? 1 : 0;
                    }
                }
                else if (!b.Hands && b.Habitat == Habitat.Land && Interactions.Tame(ref b, s))
                {
                    pets++;
                    idlePets += s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Graze ? 1 : 0;
                }
            }
            return $"{grown} grown-ups ({homeward} homeward), {children} children, {pets} tame animals ({idlePets} idle)";
        }

        /// <summary>Moves the player to a spot, standing on the floor there.</summary>
        void MovePlayer(in Punchy.Terrain.TerrainSource source, float2 xz)
        {
            // (The player is in the world the warp keys move it in, not the creatures' one.)
            var pem = queryWorld.EntityManager;
            var pe = playerQuery.GetSingletonEntity();
            var pt = pem.GetComponentData<LocalTransform>(pe);
            pt.Position = new float3(xz.x, source.FloorUnder(new float3(xz.x, pt.Position.y + 50f, xz.y), out _) + 0.1f, xz.y);
            pem.SetComponentData(pe, pt);
            pem.SetComponentData(pe, new PlayerMotion { Grounded = true });
        }

        /// <summary>
        /// Where to look at a rider from: its right and a little ahead first, then round it in eighths, the first eye with
        /// nothing built or grown between it and the rider (in a town a wall is often a step away).
        /// </summary>
        static float3 ShotEye(in Punchy.Terrain.TerrainSource source, BlobAssetReference<Punchy.Flora.FloraBlob> flora, float3 target, float yaw, float scale)
        {
            var first = float3.zero;
            for (int k = 0; k < 8; k++)
            {
                var a = yaw + math.radians(62f) + k * math.PI / 4f;
                var eye = target + (new float3(math.sin(a), 0f, math.cos(a)) * 2.73f + new float3(0f, 0.5f, 0f)) * scale;
                if (k == 0)
                    first = eye;
                var clear = true;
                for (int i = 1; i <= 6 && clear && flora.IsCreated; i++)
                    clear = !FloraPlacement.Blocked(ref flora.Value, source, math.lerp(target.xz, eye.xz, i / 6f), 0.3f);
                if (clear)
                    return eye;
            }
            return first;
        }

        /// <summary>A spot in shallow water (0.6 to 1 m: past a dog's belly, short of swimming) within 600 m, for the wade case.</summary>
        static bool FindWade(in Punchy.Terrain.TerrainSource source, float3 near, out float3 spot)
        {
            for (var r = 5f; r <= 600f; r += 3f)
                for (int k = 0; k < 48; k++)
                {
                    var a = k * math.PI * 2f / 48f;
                    var xz = near.xz + new float2(math.sin(a), math.cos(a)) * r;
                    var g = source.SampleGround(xz);
                    var depth = g.HasWater ? g.WaterSurface - g.Height : 0f;
                    if (depth > 0.6f && depth < 1f)
                    {
                        spot = new float3(xz.x, g.Height, xz.y);
                        return true;
                    }
                }
            spot = near;
            return false;
        }
    }
}
