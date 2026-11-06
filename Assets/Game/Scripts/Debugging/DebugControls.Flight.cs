using System.Collections.Generic;
using Punchy.CameraRig;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Debugging
{
    // DebugControls, -flycave, -watch, -pose and -interact. Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        /// <summary>A-69 "-flycave": the route still to fly (outside, mouth, the passage's nodes in to a chamber and back out).</summary>
        static readonly List<float3> flyRoute = new();
        static int flyLeg, flyCycles, flyCycle, flyStuck, flyLanded, flyTimeouts, flyBlocked0, flyExits;
        static bool flyOn;
        /// <summary>Which legs of a cycle are the way out at the mouth, and the cycle's length.</summary>
        static int flyMouthOut, flyCycleLength, flyClimbLeg, flyClimbs;
        static float flyLegSince, flyStillSince;
        static float3 flyStillAt;
        static bool flyStillCounted, flyDown;
        /// <summary>Frame times (ms) and speeds over the last 3 s, and the window after a mouth crossing still being filled.</summary>
        static readonly Queue<float2> flyRecent = new();
        static readonly List<float2> flyAfter = new();
        /// <summary>The climb out (a steep, fast climb away over new ground, where chunks stream in): its frames, and frames on the wing but held still.</summary>
        static readonly List<float2> flyClimb = new();
        static int flyClimbHeld;
        /// <summary>"-flyhitch MS": a main-thread stall this long every 45 frames of a climb (a chunk load's hitch on
        /// Ultra, which the editor's capped frames never show), and climb frames that fell off the wing.</summary>
        static int flyHitchMs, flyClimbFrame, flyClimbDropped;
        static float flyAfterUntil;
        static string flyExitPre;
        static float3 flyLastPos;
        /// <summary>A "-flycave" spec waiting for the flying body to land, and since when.</summary>
        static string flyPending;
        static float flyPendingSince;

        /// <summary>
        /// A-69 "-flycave N" or "-flycave N:Style": the player (a flying body, "-body Bat") flies a set route through the
        /// first cave (of that style) with a chamber, N times: from 10 m outside its mouth, in node by node to the chamber
        /// and back out, steered like a stick and the climb or descend buttons. Logs each stuck event (on the wing but
        /// under 1 m/s for a quarter second), each landing on the way, and frame times and speeds 3 s either side of
        /// each exit by the mouth.
        /// </summary>
        void StartFlyCave(in TerrainSource source, string spec)
        {
            var parts = spec.Split(':');
            flyCycles = int.TryParse(parts[0], out var n) ? math.max(n, 1) : 1;
            var styleIndex = -1;
            if (parts.Length > 1)
            {
                var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
                var styles = CaveStyleTable.Collect(palette.Landmarks);
                styleIndex = styles.FindIndex(s => s != null && string.Equals(s.name, parts[1], System.StringComparison.OrdinalIgnoreCase));
            }
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            // ("-body Bat" lands a little after the world is up: wait for a body that flies, else the route is walked.)
            using (var flierQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>()))
                if (!flierQuery.TryGetSingleton<PlayerBody>(out var flier) || !flier.Flies)
                {
                    if (flyPending == null)
                        flyPendingSince = Time.time;
                    flyPending = spec;
                    return;
                }
            flyPending = null;
            ref var set = ref source.Caves.Value;
            flyRoute.Clear();
            for (int c = 0; c < set.Caves.Length && flyRoute.Count == 0; c++)
            {
                ref var cave = ref set.Caves[c];
                if (styleIndex >= 0 && cave.Style != styleIndex + 1 || cave.NodeCount < 2)
                    continue;
                var chamber = -1;
                for (int k = 0; k < cave.NodeCount && chamber < 0; k++)
                    if (set.Kinds[cave.FirstNode + k] == CaveNodeKind.Chamber)
                        chamber = k;
                if (chamber < 0)
                    continue;
                // In at the mouth low (its sky reads as open air, so a mid-air point would sit above the lip), from
                // straight out along the passage: its first node a metre or more in (the first node is the mouth itself).
                var mouthFloor = source.FloorUnder(cave.Mouth + new float3(0f, 0.5f, 0f), out _);
                var mouth = new float3(cave.Mouth.x, mouthFloor + 1.2f, cave.Mouth.z);
                var inner = set.Nodes[cave.FirstNode + math.min(1, cave.NodeCount - 1)].Position;
                for (int k = 0; k < cave.NodeCount; k++)
                    if (math.distance(set.Nodes[cave.FirstNode + k].Position.xz, cave.Mouth.xz) > 1f)
                    {
                        inner = set.Nodes[cave.FirstNode + k].Position;
                        break;
                    }
                var outward = math.normalizesafe(cave.Mouth.xz - inner.xz, new float2(0f, 1f));
                var outside = new float3(mouth.x + outward.x * 10f, source.SampleHeight(mouth.xz + outward * 10f) + 4f, mouth.z + outward.y * 10f);
                outside.y = math.max(outside.y, mouth.y + 2f);
                // A doorstep 3 m out on the passage's axis, so it comes at the mouth lined up, not from the side.
                var doorstep = new float3(mouth.x + outward.x * 3f, 0f, mouth.z + outward.y * 3f);
                doorstep.y = math.max(mouth.y, source.SampleHeight(doorstep.xz) + 1.2f);
                for (int cycle = 0; cycle < flyCycles; cycle++)
                {
                    flyRoute.Add(doorstep);
                    flyRoute.Add(mouth);
                    // (Nodes at the mouth itself are left out: under open sky their mid-air point sits above the lip.)
                    for (int k = 0; k <= chamber; k++)
                        if (math.distance(set.Nodes[cave.FirstNode + k].Position.xz, cave.Mouth.xz) > 1f)
                            flyRoute.Add(MidAir(source, set.Nodes[cave.FirstNode + k].Position));
                    for (int k = chamber - 1; k >= 0; k--)
                        if (math.distance(set.Nodes[cave.FirstNode + k].Position.xz, cave.Mouth.xz) > 1f)
                            flyRoute.Add(MidAir(source, set.Nodes[cave.FirstNode + k].Position));
                    if (cycle == 0)
                        flyMouthOut = flyRoute.Count;
                    flyRoute.Add(mouth);
                    flyRoute.Add(doorstep);
                    flyRoute.Add(outside);
                    // Then a steep climb away, 60 m out and 30 m up (Zach's control loss: flying up fast out of a cave
                    // while a big chunk of land loads).
                    if (cycle == 0)
                        flyClimbLeg = flyRoute.Count;
                    var away = outside.xz + outward * 60f;
                    flyRoute.Add(new float3(away.x, math.max(outside.y, source.SampleHeight(away)) + 30f, away.y));
                    if (cycle == 0)
                        flyCycleLength = flyRoute.Count;
                }
                var em = queryWorld.EntityManager;
                var t = em.GetComponentData<LocalTransform>(player);
                t.Position = outside;
                t.Rotation = quaternion.RotateY(math.atan2(-outward.x, -outward.y));
                em.SetComponentData(player, t);
                using var bodyQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
                bodyQuery.TryGetSingleton<PlayerBody>(out var body);
                var flySpeed = body.FlySpeed;
                em.SetComponentData(player, new PlayerMotion { Flying = body.Flies, Airspeed = math.max(flySpeed, 1f) });
                Debug.Log($"DebugControls: flycave cave {c} (style {cave.Style}), chamber node {chamber}, mouth ({V(cave.Mouth.x)}, {V(cave.Mouth.y)}, {V(cave.Mouth.z)}), " +
                          $"{flyRoute.Count} legs over {flyCycles} cycles, body flies {body.Flies} at {V(flySpeed)} m/s");
            }
            if (flyRoute.Count == 0)
            {
                Debug.LogWarning($"DebugControls: flycave found no cave with a chamber ('{spec}')");
                return;
            }
            flyOn = true;
            flyLeg = flyCycle = flyStuck = flyLanded = flyTimeouts = flyExits = 0;
            flyBlocked0 = PlayerMovementSystem.FlyBlocked.Data;
            flyLegSince = flyStillSince = Time.time;
            flyStillAt = float3.zero;
            flyRecent.Clear();
            flyAfter.Clear();
            flyAfterUntil = 0f;
            flyClimb.Clear();
            flyClimbHeld = flyClimbs = 0;
        }

        /// <summary>A-69 "-flycave": steers the flying player along the route and counts what goes wrong.</summary>
        void FlyCave()
        {
            if (flyPending != null && EnsureLocations(out var pendingSource))
            {
                if (Time.time - flyPendingSince > 30f)
                {
                    Debug.LogWarning("DebugControls: flycave: the body never flew (pass a flying body, e.g. -body Bat)");
                    flyPending = null;
                }
                else
                    StartFlyCave(pendingSource, flyPending);
            }
            if (!flyOn || queryWorld == null || !queryWorld.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            var at = em.GetComponentData<LocalTransform>(player).Position;
            var motion = em.GetComponentData<PlayerMotion>(player);
            var ms = Time.unscaledDeltaTime * 1000f;
            var speed = Time.deltaTime > 0f ? math.distance(at, flyLastPos) / Time.deltaTime : 0f;
            flyLastPos = at;
            flyRecent.Enqueue(new float2(ms, speed));
            while (flyRecent.Count > 0 && flyRecent.Count > 3f / math.max(Time.unscaledDeltaTime, 1e-3f))
                flyRecent.Dequeue();
            if (flyAfterUntil > 0f)
            {
                flyAfter.Add(new float2(ms, speed));
                if (Time.time >= flyAfterUntil)
                {
                    flyAfterUntil = 0f;
                    Debug.Log($"DebugControls: flycave exit {flyExits}: before {flyExitPre}; after {FlyWindow(flyAfter)}; SurfaceHidden {CaveSystem.SurfaceHidden:F2}");
                }
            }

            if (flyLeg % flyCycleLength == flyClimbLeg)
            {
                flyClimb.Add(new float2(ms, speed));
                flyClimbHeld += motion.Flying && speed < 1f ? 1 : 0;
                flyClimbDropped += motion.Flying ? 0 : 1;
                if (flyHitchMs > 0 && ++flyClimbFrame % 45 == 0)
                    System.Threading.Thread.Sleep(flyHitchMs);
            }
            var target = flyRoute[flyLeg];
            var to = target - at;
            var horizontal = math.length(to.xz);
            // (On the last leg, held till the last exit's frame times are in.)
            // (Arrived within 2 m across and 1 m up or down: a sphere let it tick off the mouth from above the lip.)
            var reached = horizontal < 2f && math.abs(to.y) < 1f;
            var arrived = reached || Time.time - flyLegSince > 25f;
            if (arrived && flyLeg == flyRoute.Count - 1 && flyAfterUntil > 0f)
                arrived = false;
            if (arrived)
            {
                if (!reached)
                {
                    flyTimeouts++;
                    Debug.Log($"DebugControls: flycave leg {flyLeg} TIMED OUT at ({V(at.x)}, {V(at.y)}, {V(at.z)}), {V(math.length(to))} m short");
                }
                if (flyLeg % flyCycleLength == flyClimbLeg)
                {
                    flyClimbs++;
                    Debug.Log($"DebugControls: flycave climb {flyClimbs}: {FlyWindow(flyClimb)}, {flyClimbHeld} frames on the wing held under 1 m/s, " +
                              $"{flyClimbDropped} off the wing, hitch {flyHitchMs} ms, {(reached ? "reached" : "TIMED OUT")}");
                    flyClimb.Clear();
                    flyClimbHeld = flyClimbDropped = flyClimbFrame = 0;
                }
                if (flyLeg % flyCycleLength == flyMouthOut)
                {
                    // Out by the mouth: frame times and speeds for 3 s either side.
                    flyExits++;
                    flyExitPre = FlyWindow(new List<float2>(flyRecent));
                    flyAfter.Clear();
                    flyAfterUntil = Time.time + 3f;
                }
                flyLeg++;
                flyLegSince = Time.time;
                if (flyLeg >= flyRoute.Count)
                {
                    flyOn = false;
                    PlayerInputSystem.TestMove = null;
                    PlayerInputSystem.TestPitch = null;
                    PlayerInputSystem.TestClimb = PlayerInputSystem.TestCrouch = false;
                    Debug.Log($"DebugControls: flycave DONE {flyCycles} cycles: {flyStuck} stuck, {flyLanded} landed, {flyTimeouts} legs timed out, " +
                              $"{PlayerMovementSystem.FlyBlocked.Data - flyBlocked0} blocked frames, {flyExits} exits");
                    return;
                }
                target = flyRoute[flyLeg];
                to = target - at;
                horizontal = math.length(to.xz);
            }
            // Down on the way: counted, and off again.
            if (!motion.Flying)
            {
                if (motion.Grounded && !flyDown)
                {
                    flyDown = true;
                    flyLanded++;
                    var gap = "";
                    if (flyLanded <= 6 && EnsureLocations(out var landSource))
                    {
                        // (Where it came down: how much air is above it, and where the leg's target sits in its passage.)
                        var ceiling = CeilingAbove(landSource, at);
                        var targetFloor = landSource.FloorUnder(target + new float3(0f, 0.5f, 0f), out _);
                        gap = $", air above {V(ceiling)} m, pitch {motion.Pitch:F2}; target ({V(target.x)}, {V(target.y)}, {V(target.z)}) " +
                              $"floor {V(targetFloor)}, air above target floor {V(CeilingAbove(landSource, new float3(target.x, targetFloor, target.z)))} m";
                    }
                    Debug.Log($"DebugControls: flycave LANDED on leg {flyLeg} at ({V(at.x)}, {V(at.y)}, {V(at.z)}){gap}");
                }
                // Close and level, it walks on (taking off there, nose up, lifts it over a low mouth); otherwise off again.
                var walkOn = horizontal > 2f && horizontal < 6f && to.y < 0.8f;
                PlayerInputSystem.TestMove = horizontal > 0.1f ? to.xz / horizontal : float2.zero;
                PlayerInputSystem.TestJump = motion.Grounded && !walkOn;
                return;
            }
            flyDown = false;
            // Stuck: on the wing but hardly moving for a quarter second (counted once till it moves again).
            if (math.distance(at, flyStillAt) > 0.25f)
            {
                flyStillAt = at;
                flyStillSince = Time.time;
                flyStillCounted = false;
            }
            else if (!flyStillCounted && Time.time - flyStillSince > 0.25f)
            {
                flyStillCounted = true;
                flyStuck++;
                Debug.Log($"DebugControls: flycave STUCK on leg {flyLeg} at ({V(at.x)}, {V(at.y)}, {V(at.z)}), pitch {motion.Pitch:F2}, airspeed {V(motion.Airspeed)}");
            }
            PlayerInputSystem.TestMove = horizontal > 0.1f ? to.xz / horizontal : float2.zero;
            var angle = math.atan2(to.y, math.max(horizontal, 0.5f));
            PlayerInputSystem.TestClimb = angle > 0.3f;
            PlayerInputSystem.TestCrouch = angle < -0.3f;
            PlayerInputSystem.TestPitch = math.clamp(angle, -0.3f, 0.3f) / 0.6f;
        }

        /// <summary>A-69 "-flycave": a route point lifted to the middle of its passage's air (up to 3 m off the floor), as a player flies it.</summary>
        static float3 MidAir(in TerrainSource source, float3 p)
        {
            var floor = source.FloorUnder(p + new float3(0f, 0.5f, 0f), out _);
            var air = CeilingAbove(source, new float3(p.x, floor, p.z));
            return new float3(p.x, floor + math.clamp(air * 0.5f, 0.8f, 3f), p.z);
        }

        /// <summary>A-69: how far up from a point the air goes before rock (the cave's or the ground's), up to 30 m.</summary>
        static float CeilingAbove(in TerrainSource source, float3 p)
        {
            for (float up = 0.1f; up < 30f; up += 0.1f)
            {
                var q = p + new float3(0f, up, 0f);
                if (math.min(source.CaveDistance(q), source.SampleHeight(q.xz) - q.y) > 0f)
                    return up;
            }
            return 30f;
        }

        /// <summary>A frame-time and speed summary: mean and worst frame (ms), frames over 50 ms, slowest speed (m/s).</summary>
        static string FlyWindow(List<float2> frames)
        {
            if (frames.Count == 0)
                return "no frames";
            float sum = 0f, worst = 0f, slowest = float.MaxValue;
            var hitches = 0;
            foreach (var f in frames)
            {
                sum += f.x;
                worst = math.max(worst, f.x);
                slowest = math.min(slowest, f.y);
                hitches += f.x > 50f ? 1 : 0;
            }
            return $"{frames.Count} frames, mean {V(sum / frames.Count)} ms, worst {V(worst)} ms, {hitches} over 50 ms, slowest {V(slowest)} m/s";
        }

        /// <summary>
        /// W-67: the floor a walker at the -walk target should stand on: its y if the leg gave one ("x,y,z"), else the
        /// higher of the ground under the walker's height and the highest kit top there (within 2.5 m above the feet,
        /// with headroom over it), so standing under a deck doesn't count as arriving on it.
        /// </summary>
        bool ExpectedFloor(float3 target, float3 feet, out float expected)
        {
            expected = target.y;
            if (target.y != 0f)
                return true;
            if (!EnsureLocations(out var ground))
                return false;
            expected = ground.FloorUnder(new float3(target.x, feet.y + 0.5f, target.z), out _);
            // W-67b: the first kit floor over that ground (not just one within 2.5 m of the feet: a walker 2.6 m under
            // a wharf deck missed it and was ARRIVED on the seabed). Upper floors need an explicit "x,y,z".
            using var kitQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
            if (kitQuery.TryGetSingletonBuffer<KitBox>(out var kit, true) &&
                KitCollision.LowestTopAt(kit, target.xz, expected - 0.3f, 1.7f, out var kitTop, RoofGap(target.xz)) && kitTop > expected)
                expected = kitTop;
            return true;
        }

        /// <summary>
        /// W-67c (#38): for KitCollision.LowestTopAt from 0.3 m under the ground: on dry ground a box with a body's
        /// height (1.7 m) clear under it is a roof, not a floor; under water any box over the seabed is the floor.
        /// </summary>
        float RoofGap(float2 xz)
        {
            if (!EnsureLocations(out var source))
                return float.MaxValue;
            var g = source.SampleGround(xz);
            return g.HasWater && g.WaterSurface > g.Height ? float.MaxValue : 0.3f + 1.7f;
        }

        /// <summary>
        /// Points the camera at the watched species: the nearest one doing something worth seeing (climbing, flying,
        /// in a web, an idle action), else the nearest; re-picked every few seconds. A-34: "*" for any species
        /// ("*:Merchant": OgreCamp's merchants are goblins, so "Ogre:Merchant" found none).
        /// </summary>
        void Watch()
        {
            if (watchSpecies == null || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            if (creatureWorld != queryWorld)
            {
                creatureWorld = queryWorld;
                creatureQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            }
            if (watchDirector == null)
                watchDirector = FindAnyObjectByType<FaunaDirector>();
            // "Human:Hunt": only those with that task.
            var watchParts = watchSpecies.Split(':');
            var anySpecies = watchParts[0] == "*";
            var index = anySpecies ? int.MaxValue : watchDirector != null ? watchDirector.SpeciesIndex(watchParts[0]) : -1;
            var watchTask = CreatureTask.None;
            var hasTask = watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchTask);
            // ("Human:Baker": only those with that job, A-29.)
            var watchJob = FolkJob.None;
            var hasJob = !hasTask && watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchJob);
            // ("Deer:Drink": only those doing that, A-49 #64.)
            var watchDoing = CreatureMode.Wander;
            var hasDoing = !hasTask && !hasJob && watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchDoing);
            // ("Deer:Pant": only those doing that idle action, re-picked every second, A-71.)
            watchAction = IdleAction.None;
            var hasAction = !hasTask && !hasJob && !hasDoing && watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchAction);
            if (index < 0 || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            if (Time.unscaledTime >= nextWatchPick || !em.Exists(watched))
            {
                nextWatchPick = Time.unscaledTime + (hasAction ? 1f : 8f);
                var entities = creatureQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                var best = float.MaxValue;
                watched = Entity.Null;
                var wrongSpecies = "";
                foreach (var e in entities)
                {
                    var species = em.GetComponentData<CreatureBody>(e).Species;
                    var c = em.GetComponentData<CreatureState>(e);
                    if (hasTask && c.Task != watchTask || hasJob && FolkJobs.Of(c) != watchJob || hasDoing && c.Mode != watchDoing || hasAction && c.Action != watchAction)
                        continue;
                    if (!anySpecies && species != index)
                    {
                        // (Kept to say who does hold that job, if the asked species has none.)
                        if ((hasTask || hasJob) && wrongSpecies.Length < 80)
                            wrongSpecies += $" {watchDirector.SpeciesAt(species)?.name}";
                        continue;
                    }
                    var g = terrainQuery.GetSingleton<TerrainSource>().SampleGround(c.Position.xz);
                    var swimming = g.HasWater && c.Position.y < g.WaterSurface - 0.2f;
                    var busy = swimming || c.Mode == CreatureMode.Flee && c.Speed > 2f || c.Mode == CreatureMode.Climb || c.Mode == CreatureMode.Web || c.Fly > 0.5f ||
                               c.Action != IdleAction.None || c.Mode == CreatureMode.Hunt || c.Mode == CreatureMode.Burrow || c.Mode == CreatureMode.Roost;
                    var score = math.distance(c.Position, player.Position) - (busy ? 1000f : 0f) - (c.Fly > 0.5f ? 1000f : 0f);
                    if (score < best)
                    {
                        best = score;
                        watched = e;
                    }
                }
                entities.Dispose();
                if (watched == Entity.Null && wrongSpecies.Length > 0 && watchWarned != watchSpecies)
                {
                    watchWarned = watchSpecies;
                    Debug.LogWarning($"DebugControls: no {watchSpecies}; that job is held by:{wrongSpecies} (try -watch *:{watchParts[1]})");
                }
                if (watchAirFrames > 0)
                    Debug.Log($"DebugControls: steepest flight over {watchAirFrames} airborne frames: {math.degrees(math.atan(watchSteepest)):F0}°");
                watchSteepest = 0f;
                watchAirFrames = 0;
                watchLast = Vector3.zero;
                if (watched != Entity.Null)
                {
                    var c = em.GetComponentData<CreatureState>(watched);
                    Debug.Log($"DebugControls: watching {watchSpecies} ({c.Mode}, stage {c.Stage}, action {c.Action}, fly {c.Fly:F1}, " +
                              $"height above ground {c.Position.y - terrainQuery.GetSingleton<TerrainSource>().SampleHeight(c.Position.xz):F1} m, " +
                              $"player {math.distance(c.Position.xz, player.Position.xz):F0} m away, home {math.distance(c.Position.xz, c.Home):F0} m (leash {c.Leash:F0}), " +
                              // (A folk at work keeps TargetHeight 0, which read as "119 m down" in A-33: not a bad position.)
                              $"target {math.distance(c.Position.xz, c.Target):F2} m across, {(c.TargetHeight != 0f ? $"{c.TargetHeight - c.Position.y:F2} m up" : "no height")}, speed {c.Speed:F2}, " +
                              $"{watchDirector.SpeciesAt(em.GetComponentData<CreatureBody>(watched).Species)?.name} job {FolkJobs.Of(c)}, " +
                              $"yaw {math.degrees(c.Yaw):F0} deg turning {math.degrees(c.YawRate):F0} deg/s, " +
                              $"anchor {math.distance(c.Position, c.Anchor):F2} m (size {c.AnchorSize:F2}))");
                }
            }
            if (watched != Entity.Null && em.Exists(watched))
            {
                var c = em.GetComponentData<CreatureState>(watched);
                var p = (Vector3)c.Position;
                if (c.Fly > 0.5f && watchLast != Vector3.zero && Time.deltaTime > 0f)
                {
                    var d = p - watchLast;
                    var flat = new Vector2(d.x, d.z).magnitude;
                    if (d.magnitude > 0.01f)
                    {
                        var steep = math.abs(d.y) / math.max(flat, 1e-4f);
                        if (steep > 1.2f)
                            Debug.Log($"DebugControls: steep flight frame: {math.degrees(math.atan(steep)):F0}° ({c.Mode} stage {c.Stage}, " +
                                      $"speed {c.Speed:F2}, moved {flat:F3} m across, {d.y:F3} m up, {c.Position.y - terrainQuery.GetSingleton<TerrainSource>().SampleHeight(c.Position.xz):F2} m above ground)");
                        watchSteepest = math.max(watchSteepest, steep);
                        watchAirFrames++;
                    }
                }
                watchLast = p;
                // Every change of what it's doing, as it happens.
                if (c.Mode != watchMode || c.Stage != watchStage)
                {
                    Debug.Log($"DebugControls: {watchSpecies} now {c.Mode} stage {c.Stage} (target {math.distance(c.Position.xz, c.Target):F1} m)");
                    watchMode = c.Mode;
                    watchStage = c.Stage;
                    // A hunter drawing back and throwing: worth a picture.
                    if (shotDir != null && c.Mode == CreatureMode.Hunt && (c.Stage == FolkHunting.Aim || c.Stage == FolkHunting.Throw))
                        Shot($"hunt-{c.Stage}-{++watchShots}.png");
                }
                if (watchFrom.HasValue)
                {
                    // From a fixed spot above the ground (it stays put across the shot list's lines: watchFromAt isn't
                    // cleared between them), looking at the animal, or at the ground over it.
                    var terrain = terrainQuery.GetSingleton<TerrainSource>();
                    var look = new Vector3(p.x, math.max(p.y, terrain.SampleHeight(c.Position.xz)), p.z);
                    var range = watchFrom.Value.y;
                    if (!watchFromAt.HasValue || math.distance(((float3)watchFromAt.Value).xz, ((float3)look).xz) > range * 2f)
                    {
                        // (Off to the side of where it's heading, so its track crosses the view.)
                        var side = new float2(math.cos(c.Yaw), -math.sin(c.Yaw));
                        var xz = ((float3)look).xz + side * range;
                        watchFromAt = new Vector3(xz.x, terrain.SampleHeight(xz) + watchFrom.Value.x, xz.y);
                        Debug.Log($"DebugControls: watching from ({V(watchFromAt.Value.x)}, {V(watchFromAt.Value.y)}, {V(watchFromAt.Value.z)})");
                    }
                    PoseCamera(watchFromAt.Value, Quaternion.LookRotation(look - watchFromAt.Value));
                    ThirdPersonCamera.WatchTarget = null;
                    return;
                }
                var aloft = c.Position.y - terrainQuery.GetSingleton<TerrainSource>().SampleHeight(c.Position.xz) > 1f;
                ref var watchedBody = ref em.GetComponentData<CreatureBody>(watched).Blob.Value;
                // Upright bodies (people) are framed whole, from their middle.
                var tall = watchedBody.Upright ? watchedBody.StandHeight * c.Scale : 0f;
                ThirdPersonCamera.WatchTarget = (Vector3)c.Position + Vector3.up * math.max(0.3f, tall * 0.6f);
                ThirdPersonCamera.WatchPitch = watchPitchOverride ?? (aloft ? -15f : 15f);
                var size = math.max(watchedBody.BodyLength * c.Scale, tall * 0.6f);
                ThirdPersonCamera.WatchDistance = math.clamp(size * 9f, 0.6f, 8f) * (aloft ? 1.4f : 1f);
            }
            else
                ThirdPersonCamera.WatchTarget = null;
        }

        /// <summary>Holds the "-pose" specimen two metres to the player's side, in the pose asked for, and circles the camera round it.</summary>
        void Pose()
        {
            if (poseRequest == null || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.CanSpawn)
                return;
            var em = queryWorld.EntityManager;
            var parts = poseRequest.Split(':');
            var how = parts.Length > 1 ? parts[1] : "fly";
            // A third part is the camera's pitch (70: from above, to see a wing's shape).
            var viewPitch = parts.Length > 2 && float.TryParse(parts[2], out var p) ? p : 20f;
            // Gliding: in the air with the wings held level, half way through the beat.
            var glide = how == "glide";
            if (glide)
                how = "fly";
            // A-42: "lie": lying down (Rest), its legs folded; "-oldlie" on the same line leaves them as they fell before
            // A-42. A later -pose line naming another species swaps the specimen.
            var lie = how == "lie";
            if (posed != Entity.Null && em.Exists(posed) && em.GetComponentData<CreatureBody>(posed).Species != director.SpeciesIndex(parts[0]))
            {
                em.DestroyEntity(posed);
                posed = Entity.Null;
            }
            if (posed == Entity.Null || !em.Exists(posed))
            {
                var index = director.SpeciesIndex(parts[0]);
                if (index < 0)
                {
                    Debug.LogWarning($"DebugControls: no species '{parts[0]}' to pose");
                    poseRequest = null;
                    return;
                }
                var spot = player.Position.xz + new float2(2f, 0f);
                posed = director.Spawn(index, new float3(spot.x, source.SampleHeight(spot), spot.y), 0f);
                posedAt = Time.time;
                em.AddComponent<PosedForTest>(posed);
                Debug.Log($"DebugControls: posing {parts[0]} ({how})");
            }
            var c = em.GetComponentData<CreatureState>(posed);
            ref var b = ref em.GetComponentData<CreatureBody>(posed).Blob.Value;
            var dt = Time.deltaTime;
            var xz = player.Position.xz + new float2(2f, 0f);
            var ground = source.SampleHeight(xz);
            var size = b.BodyLength * c.Scale;
            c.Mode = how == "hang" ? CreatureMode.Roost : lie ? CreatureMode.Rest : CreatureMode.Idle;
            if (lie)
                c.Lie = 1f;
            // "scare" (A-42): stands 3 s, then turns and runs off straight ahead (+z), as if frightened: the flee state
            // change a skunk's gas starts from. The camera follows it.
            var scared = how == "scare" ? math.max(Time.time - posedAt - 3f, 0f) : 0f;
            if (scared > 0f)
            {
                c.Mode = CreatureMode.Flee;
                c.Speed = b.RunSpeed * c.Scale;
                c.GaitSpeed = c.Speed;
                CreatureMotion.AdvancePhase(ref c, ref b, dt);
                xz += new float2(0f, scared * c.Speed);
                ground = source.SampleHeight(xz);
            }
            c.Stage = (byte)(how == "hang" ? 200 : 0);
            c.Fly = how == "fly" ? 1f : 0f;
            c.Vertical = how == "hang" ? -1f : 0f;
            c.Speed = how == "fly" ? b.FlySpeed * 0.6f : scared > 0f ? c.Speed : 0f;
            var onGround = how == "rest" || lie || how == "scare" || how == "nest" && b.Nests == NestSite.Ground;
            c.Position = new float3(xz.x, ground + (onGround ? 0f : math.max(1.2f, size * 4f)), xz.y);
            // Sitting in its nest (made where it is, the size FindNest gives one).
            if (how == "nest")
            {
                c.Anchor = c.Position;
                c.AnchorSize = math.max(size * 0.7f, 0.08f) * (b.Nests == NestSite.Treetop ? 1.4f : 1f);
            }
            // Swimming: out in deep water near the player (start by a lake or river), paddling on the spot.
            if (how == "swim")
            {
                if (!swimSpot.HasValue)
                    for (var d = 3f; d < 60f && !swimSpot.HasValue; d += 2f)
                        for (int k = 0; k < 12 && !swimSpot.HasValue; k++)
                        {
                            var at = player.Position.xz + new float2(math.sin(k * 0.5236f), math.cos(k * 0.5236f)) * d;
                            var w = source.SampleGround(at);
                            if (w.HasWater && w.WaterSurface - w.Height > b.StandHeight * c.Scale * 2f)
                                swimSpot = at;
                        }
                if (swimSpot.HasValue)
                {
                    var w = source.SampleGround(swimSpot.Value);
                    var below = b.LavaDweller ? -b.FrontRadius * c.Scale * 0.4f : b.StandHeight * c.Scale + b.FrontRadius * b.BodyFlatten * c.Scale * 0.55f;
                    c.Position = new float3(swimSpot.Value.x, w.WaterSurface - below, swimSpot.Value.y);
                    c.Speed = b.WalkSpeed * c.Scale;
                    CreatureMotion.AdvancePhase(ref c, ref b, dt);
                }
            }
            c.Clock += dt;
            if (glide)
                c.Phase = 0.25f;
            else if (how == "fly")
                CreatureMotion.AdvancePhase(ref c, ref b, dt);
            em.SetComponentData(posed, c);
            // (Upright bodies, people, framed head and shoulders: their body length is just the hips.)
            var standing = b.Upright ? b.StandHeight * c.Scale : 0f;
            ThirdPersonCamera.WatchTarget = (Vector3)c.Position + Vector3.up * (b.Upright ? standing * 1.55f : size * 0.5f);
            ThirdPersonCamera.WatchPitch = viewPitch;
            ThirdPersonCamera.WatchDistance = b.Upright ? math.max(standing * 0.9f, 0.6f) : math.clamp(size * 4f, 0.5f, 6f);
        }

        /// <summary>
        /// The "-interact" test: 12 s in (after any -spawn), stands the player just in front of the nearest animal of the
        /// species, facing it; a moment later pets it or picks it up; carrying, walks off a few metres and puts it down.
        /// </summary>
        void Interact()
        {
            if (interactRequest == null || Time.timeSinceLevelLoad - optionsAt < settle + 4f || queryWorld == null || !queryWorld.IsCreated ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var playerEntity))
                return;
            var em = queryWorld.EntityManager;
            var parts = interactRequest.Split(':');
            var carry = parts.Length > 1 && parts[1] == "carry";
            // ("Rabbit:carry:sneak": crept up on from behind, not walked up to from the front.)
            var sneak = parts.Length > 2 && parts[2] == "sneak";
            if (interactDirector == null)
                interactDirector = FindAnyObjectByType<FaunaDirector>();
            var index = interactDirector != null ? interactDirector.SpeciesIndex(parts[0]) : -1;
            if (index < 0)
            {
                Debug.LogWarning($"DebugControls: no species '{parts[0]}' to interact with");
                interactRequest = null;
                return;
            }
            // (The queries made once per world, not every frame.)
            if (creatureWorld != queryWorld)
            {
                creatureWorld = queryWorld;
                creatureQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            }
            if (requestWorld != queryWorld)
            {
                requestWorld = queryWorld;
                requestQuery = em.CreateEntityQuery(ComponentType.ReadWrite<InteractionRequest>());
            }
            var player = em.GetComponentData<LocalTransform>(playerEntity);
            if (!requestQuery.TryGetSingleton<InteractionRequest>(out var request))
                return;
            void Send(InteractAct act)
            {
                request.Act = act;
                request.Serial++;
                request.Target = interactTarget;
                // (Joined to someone else's game: the server knows the animal by its ghost id.)
                request.TargetGhost = em.HasComponent<Unity.NetCode.GhostInstance>(interactTarget)
                    ? em.GetComponentData<Unity.NetCode.GhostInstance>(interactTarget).ghostId : 0;
                requestQuery.SetSingleton(request);
                Debug.Log($"DebugControls: interact {act} {parts[0]} (ghost {request.TargetGhost}, {math.distance(em.GetComponentData<CreatureState>(interactTarget).Position.xz, em.GetComponentData<LocalTransform>(playerEntity).Position.xz):F1} m away)");
            }

            var now = Time.timeSinceLevelLoad;
            if (interactStep < 0f)
            {
                // Find one and stand in front of it.
                var entities = creatureQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                var best = float.MaxValue;
                interactTarget = Entity.Null;
                foreach (var e in entities)
                {
                    if (em.HasComponent<PlayerAvatar>(e) || em.GetComponentData<CreatureBody>(e).Species != index)
                        continue;
                    var d = math.distance(em.GetComponentData<CreatureState>(e).Position, player.Position);
                    if (d < best)
                    {
                        best = d;
                        interactTarget = e;
                    }
                }
                entities.Dispose();
                if (interactTarget == Entity.Null)
                    return;
                interactStep = now;
            }
            if (!em.Exists(interactTarget))
            {
                interactRequest = null;
                return;
            }
            var s = em.GetComponentData<CreatureState>(interactTarget);
            var elapsed = now - interactStep;
            if (elapsed < 1f)
            {
                // Keep stepping up to it (it may be wandering) until the request goes.
                var toward = sneak ? new float2(math.sin(s.Yaw), math.cos(s.Yaw))
                                   : math.normalizesafe(s.Position.xz - player.Position.xz, new float2(0f, 1f));
                var stand = s.Position.xz - toward * (sneak ? 1.3f : 0.9f);
                player.Position = new float3(stand.x, s.Position.y, stand.y);
                player.Rotation = quaternion.RotateY(math.atan2(toward.x, toward.y));
                em.SetComponentData(playerEntity, player);
                ThirdPersonCamera.WatchTarget = (Vector3)player.Position + Vector3.up * 0.4f;
                ThirdPersonCamera.WatchPitch = watchPitchOverride ?? 20f;
                ThirdPersonCamera.WatchDistance = 3f;
                return;
            }
            if (request.Act == InteractAct.None || request.Target != interactTarget)
            {
                Send(carry ? InteractAct.PickUp : InteractAct.Pet);
                return;
            }
            if (elapsed > 3f && (elapsed - Time.deltaTime) <= 3f)
            {
                var avatarQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<CreatureState>());
                var avatar = avatarQuery.TryGetSingleton<CreatureState>(out var a) ? $"{a.Action} at {a.ActionTime:F2}" : "none";
                Debug.Log($"DebugControls: {parts[0]} is {s.Mode} (stage {s.Stage}, holder {s.Holder}); player pose {request.Pose}, body {avatar}");
                // #16: what it weighs against what a carrier can lift.
                ref var wb = ref em.GetComponentData<CreatureBody>(interactTarget).Blob.Value;
                var kg = wb.WeightAt(s.Scale);
                Debug.Log($"DebugControls: carry check: {parts[0]} weighs {kg:F1} kg (scale {s.Scale:F2}), capacity {Interactions.CarryCapacity:F0} kg: " +
                          (kg <= Interactions.CarryCapacity ? "light enough" : "too heavy, can't lift") + $"; held: {s.Mode == CreatureMode.Held}" +
                          $"; tame (settled, leash {s.Leash:F0} m): {Interactions.Tame(ref wb, s)}");
            }
            // Its own screenshots: being petted or just picked up, then (carrying) walking, turning on the spot and
            // sprinting.
            foreach (var at in new[] { 2.5f, 5f, 6.6f, 8.2f })
                if (shotDir != null && elapsed > at && elapsed - Time.deltaTime <= at)
                    Shot($"interact-{parts[0]}-{(carry ? "carry" : "pet")}-{at * 10f:F0}.png");
            // Carrying: walk on (as if the stick were pushed), turn round on the spot, sprint, then put it down.
            Punchy.Player.PlayerInputSystem.TestMove = null;
            Punchy.Player.PlayerInputSystem.TestSprint = false;
            if (carry && s.Mode == CreatureMode.Held && elapsed > 4f && elapsed < 8.6f)
            {
                var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz;
                if (elapsed < 6f || elapsed > 7f)
                {
                    Punchy.Player.PlayerInputSystem.TestMove = math.normalizesafe(forward, new float2(0f, 1f));
                    Punchy.Player.PlayerInputSystem.TestSprint = elapsed > 7f;
                }
                else
                {
                    player.Rotation = math.mul(player.Rotation, quaternion.RotateY(math.PI * Time.deltaTime));
                    em.SetComponentData(playerEntity, player);
                }
            }
            if (carry && elapsed > 9.5f && request.Act == InteractAct.PickUp)
                Send(InteractAct.PutDown);
            ThirdPersonCamera.WatchTarget = (Vector3)player.Position + Vector3.up * 0.8f;
            if (elapsed > 14f)
            {
                Debug.Log($"DebugControls: interaction test over: {parts[0]} is {s.Mode}");
                interactRequest = null;
                ThirdPersonCamera.WatchTarget = null;
            }
        }
    }
}
