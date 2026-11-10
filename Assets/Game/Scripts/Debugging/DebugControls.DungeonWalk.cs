using System.Collections.Generic;
using System.Text;
using Punchy.Dungeon;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    // DebugControls, -dungeonwalk (A-95, #24): a scripted walk through dungeons that logs collision problems.
    public partial class DebugControls
    {
        /// <summary>A-95: one problem the walk met.</summary>
        struct WalkProblem
        {
            public string Type, Body, Kind, Near;
            public float3 At;
            public int Dungeon, Frames;
            public int Severity => Kind switch { "fall" => 4, "inrock" => 4, "stuck" => 3, "timeout" => 3, "socket" => 2, _ => 1 };
        }

        struct WalkDungeon
        {
            public Entity Entity;
            public string Type;
            public uint Id;
        }

        static string dwPending;
        static int dwSeenCount;
        static float2 dwOwnProp = new float2(1e9f, 1e9f);
        static bool dwOwnIsChest;
        static int dwFlyBlocked0;
        static float dwClosest;
        static float dwSeenSince;
        static float dwPendingSince;
        static bool dwOn;
        static int dwCount = 5, dwLegCap = 40;
        static readonly List<string> dwBodies = new();
        static readonly List<WalkDungeon> dwDungeons = new();
        static readonly List<float3> dwRoute = new();
        // Extra reach per socket leg: a boxed prop stands on the socket, so the body stops at the box's side.
        static readonly Dictionary<int, float> dwSlack = new();
        static readonly List<WalkProblem> dwProblems = new();
        static readonly Dictionary<string, int> dwProblemIndex = new();
        static readonly Dictionary<string, int[]> dwCounts = new();
        static readonly List<Transform> dwProps = new();
        static readonly List<(float3 c, quaternion r)> dwChests = new();
        static int dwBody, dwDungeon, dwLeg, dwPhase;
        static float dwPhaseSince, dwLegSince, dwStillSince, dwRockSince, dwFloorMin;
        static float3 dwStillAt, dwPrevAt;
        static bool dwJumped, dwFlier, dwSkipping, dwHavePrev, dwCrossed;
        static float dwSize = 1f;
        static string dwBodyWas, dwPrefWas;

        // Counter slots per "type/body".
        const int CDungeons = 0, CLegs = 1, CWalked = 2, CSkipped = 3, CFalls = 4, CStuck = 5, CTimeouts = 6, CBodyClips = 7,
            CCamClips = 8, CSockets = 9, CPokes = 10, CDrops = 11, CCircles = 12, CSlots = 13;

        /// <summary>
        /// A-95 "-dungeonwalk N[:Body,Body,...[:legs]]": for each body (default Human, Rabbit, Crow) the player walks (or
        /// flies) a route through the first N caves, N buried ruins and N sunken temples: in at the mouth, along the
        /// passages to each chamber and room, out to its floor sockets and back. Logs every fall-through (more than 2 m
        /// under the leg's lowest floor, or 0.5 s in rock), every stuck spot (under 0.3 m of progress in 3 s while
        /// pushing, a jump tried at 1.5 s), every leg that times out, and every clip (the body's middle or the camera
        /// inside a kit box, a dungeon prop, a chest, the temple's solid or rock), with where and what. Legs whose
        /// straight line is not walkable (rock, a box, a ledge, a drop) are teleported and counted as skipped.
        /// </summary>
        void StartDungeonWalk(string spec)
        {
            var parts = spec.Split(':');
            dwCount = parts.Length > 0 && int.TryParse(parts[0], out var n) ? math.max(n, 1) : 5;
            dwBodies.Clear();
            dwBodies.AddRange(parts.Length > 1 && parts[1].Length > 0 ? parts[1].Split(',') : new[] { "Human", "Rabbit", "Crow" });
            dwLegCap = parts.Length > 2 && int.TryParse(parts[2], out var cap) ? math.max(cap, 4) : 40;
            dwPending = spec;
            dwPendingSince = Time.unscaledTime;
        }

        bool GatherDungeons()
        {
            if (queryWorld == null || !queryWorld.IsCreated)
                return false;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var all = new List<WalkDungeon>();
            foreach (var e in entities)
            {
                var info = em.GetComponentData<DungeonInfo>(e);
                var sockets = em.GetBuffer<DungeonSocket>(e, true);
                var ruin = false;
                for (int i = 0; i < sockets.Length && !ruin; i++)
                    ruin = (sockets[i].Tags & (uint)SocketTags.RuinRoom) != 0;
                var type = info.Type == DungeonTypes.SunkenTemple ? "Temple" : ruin ? "Ruin" : "Cave";
                all.Add(new WalkDungeon { Entity = e, Type = type, Id = info.Id });
            }
            // (The caves' dungeons come up a couple a frame: wait for the count to hold still for 3 s.)
            if (!all.Exists(d => d.Type == "Temple") || all.Count != dwSeenCount)
            {
                dwSeenCount = all.Count;
                dwSeenSince = Time.unscaledTime;
                return false;
            }
            if (Time.unscaledTime - dwSeenSince < 3f)
                return false;
            all.Sort((a, b) => a.Id.CompareTo(b.Id));
            dwDungeons.Clear();
            foreach (var type in new[] { "Cave", "Ruin", "Temple" })
            {
                var picked = all.FindAll(d => d.Type == type);
                if (picked.Count > dwCount)
                    picked.RemoveRange(dwCount, picked.Count - dwCount);
                dwDungeons.AddRange(picked);
                WalkLog($"DebugControls: dungeonwalk {type}: {all.FindAll(d => d.Type == type).Count} in the world, walking {picked.Count}");
            }
            return true;
        }

        static bool dsPending;
        static float dsPendingSince;

        /// <summary>
        /// A-91 "-dungeonspots": once the dungeons are up, logs two camera spots per dungeon type (the first cave with a
        /// room, the first buried ruin, the sunken temple): its entrance (from outside the mouth, looking in) and its
        /// deepest room or chamber (from a corner, looking across), each as a ready shot-list line
        /// ("-goto x,y,z -camlook ...") for before/after shots and frame times on any branch.
        /// </summary>
        void DungeonSpots(in TerrainSource source)
        {
            var em = queryWorld.EntityManager;
            ref var set = ref source.Caves.Value;
            foreach (var type in new[] { "Cave", "Ruin", "Temple" })
            {
                var found = false;
                foreach (var d in dwDungeons)
                {
                    if (d.Type != type)
                        continue;
                    if (type == "Temple")
                    {
                        var sockets = em.GetBuffer<DungeonSocket>(d.Entity, true);
                        if (sockets.Length == 0 || !source.Temple(sockets[0].Pos.xz, out var layout))
                            continue;
                        ref var rooms = ref source.Landmarks.Value.RuinRooms;
                        int hi = 0, lo = 0;
                        for (int i = 1; i < rooms.Length; i++)
                        {
                            if (rooms[i].Floor > rooms[hi].Floor) hi = i;
                            if (rooms[i].Floor < rooms[lo].Floor) lo = i;
                        }
                        var tl = layout;
                        var allRooms = source.Landmarks;
                        float3 Corner(int i, bool far, float y)
                        {
                            var r = allRooms.Value.RuinRooms[i];
                            var u = far ? r.U1 - 0.7f : r.U0 + 0.7f;
                            var v = far ? r.V1 - 0.7f : r.V0 + 0.7f;
                            return SunkenTempleShape.World(tl, new float3(u, r.FloorAt(u) + y, v));
                        }
                        SpotLine(type, d.Id, "entrance", Corner(hi, false, 0.2f), Corner(hi, false, 1.7f), Corner(hi, true, 0.6f));
                        SpotLine(type, d.Id, "deep", Corner(lo, false, 0.2f), Corner(lo, false, 1.7f), Corner(lo, true, 0.6f));
                        found = true;
                        break;
                    }
                    var c = -1;
                    for (int i = 0; i < set.Caves.Length && c < 0; i++)
                        if (set.Caves[i].Key == d.Id)
                            c = i;
                    if (c < 0 || (type == "Cave" && set.Caves[c].RoomCount == 0))
                        continue;
                    ref var cave = ref set.Caves[c];
                    var mouth = Feet(source, cave.Mouth);
                    var inner = Feet(source, set.Nodes[cave.FirstNode + math.min(1, cave.NodeCount - 1)].Position);
                    var outward = math.normalizesafe(mouth.xz - inner.xz, new float2(0f, 1f));
                    var back = mouth.xz + outward * 6f;
                    var eye = new float3(back.x, math.max(source.SampleHeight(back), mouth.y) + 2.2f, back.y);
                    SpotLine(type, d.Id, "entrance", mouth, eye, inner + new float3(0f, 1f, 0f));
                    // The deepest room (the ruin's own room for a ruin), else the deepest chamber.
                    var best = -1;
                    for (int r = cave.FirstRoom; r < cave.FirstRoom + cave.RoomCount; r++)
                        if ((type != "Ruin" || set.Rooms[r].Type == (int)CaveRoomType.Ruin) && (best < 0 || set.Rooms[r].Origin.y < set.Rooms[best].Origin.y))
                            best = r;
                    if (best >= 0)
                    {
                        var room = set.Rooms[best];
                        var feet = Feet(source, room.World(new float3(-room.HX + 0.8f, 0.3f, 0.9f)));
                        SpotLine(type, d.Id, "deep", feet, room.World(new float3(-room.HX + 0.8f, 1.8f, 0.9f)),
                                 room.World(new float3(room.HX * 0.4f, 0.8f, room.HZ * 2f - 0.6f)));
                    }
                    found = true;
                    break;
                }
                if (!found)
                    WalkLog($"DebugControls: dungeonspots {type}: none");
            }
            WalkLog("DebugControls: dungeonspots DONE");
        }

        static void SpotLine(string type, uint id, string what, float3 feet, float3 eye, float3 look) =>
            WalkLog($"DebugControls: dungeonspots {type} {id} {what}: -goto {V(feet.x)},{V(feet.y)},{V(feet.z)} " +
                    $"-camlook {V(eye.x)},{V(eye.y)},{V(eye.z)},{V(look.x)},{V(look.y)},{V(look.z)}");

        /// <summary>A-95: runs the walk, a frame at a time.</summary>
        void DungeonWalk()
        {
            if (dsPending && Time.unscaledTime - dsPendingSince < 300f && EnsureLocations(out var spotSource) && GatherDungeons())
            {
                dsPending = false;
                DungeonSpots(spotSource);
            }
            if (dwPending != null)
            {
                if (Time.unscaledTime - dwPendingSince > 300f)
                {
                    WalkLog("DebugControls: dungeonwalk: the dungeons never came up");
                    dwPending = null;
                    return;
                }
                if (!EnsureLocations(out _) || !playerQuery.TryGetSingletonEntity<LocalTransform>(out _) || !GatherDungeons())
                    return;
                dwPending = null;
                dwOn = true;
                dwBody = dwDungeon = 0;
                dwPhase = 0;
                dwPhaseSince = Time.time;
                dwProblems.Clear();
                dwProblemIndex.Clear();
                dwCounts.Clear();
                dwPrefWas = TestHooks.BodyPref();
                dwBodyWas = null;
            }
            if (!dwOn || !EnsureLocations(out var source) || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;

            // Phase 0: switch to the next body, and give the avatar a moment to rebuild.
            if (dwPhase == 0)
            {
                if (Time.time - dwPhaseSince < 0.1f)
                    return;
                var was = TestHooks.SetBody(dwBodies[dwBody]);
                dwBodyWas ??= was;
                dwPhase = 1;
                dwPhaseSince = Time.time;
                return;
            }
            if (dwPhase == 1)
            {
                if (Time.time - dwPhaseSince < 3f)
                    return;
                StartWalkDungeon(source, player);
                return;
            }
            var t = em.GetComponentData<LocalTransform>(player);
            var at = t.Position;
            var motion = em.GetComponentData<PlayerMotion>(player);
            // Phase 2: settle after a teleport (held still).
            if (dwPhase == 2)
            {
                PlayerInputSystem.TestMove = float2.zero;
                if (dwFlier)
                    em.SetComponentData(player, new PlayerMotion { Flying = true, Airspeed = 1f });
                if (Time.time - dwPhaseSince < 1.5f)
                    return;
                dwPhase = 3;
                BeginLeg(source, at);
                return;
            }

            var d = dwDungeons[dwDungeon];
            var key = $"{d.Type}/{dwBodies[dwBody]}";
            var settled = Time.time - dwLegSince > 0.4f;
            if (settled)
                CheckClips(source, d, at);

            var target = dwRoute[dwLeg];
            var to = target - at;
            var horizontal = math.length(to.xz);
            dwClosest = math.min(dwClosest, math.length(to));
            // (A bird can't hover: within a wingspan or two of the point is there.)
            var reached = dwFlier ? horizontal < 2f && math.abs(to.y) < 1.5f : horizontal < 0.6f + (dwSlack.TryGetValue(dwLeg, out var slack) ? slack : 0f) && math.abs(to.y) < 1.6f;
            if (reached)
            {
                if (!dwSkipping)
                    dwCounts[key][CWalked]++;
                dwSkipping = false;
                NextLeg(source, player, false);
                return;
            }
            // Fall-through: well under the leg's lowest floor, or in rock for half a second.
            var centre = at + new float3(0f, 0.6f * dwSize, 0f);
            // A fall-through is the body passing down through a floor between two frames: from last frame's height, the
            // floor under this frame's spot is over the body. (Probing from a fixed height above instead could start in
            // the rock of a low shelf overhead and read that as a floor the body fell through.)
            if (settled)
            {
                var from = new float3(at.x, dwPrevAt.y + 0.1f, at.z);
                if (dwHavePrev && at.y < dwPrevAt.y && PlayerMovementSystem.SolidDistance(source, from) > 0.05f && source.FloorUnder(from, out _) > at.y + 0.05f)
                    dwCrossed = true;
                dwPrevAt = at;
                dwHavePrev = true;
            }
            if (settled && at.y < dwFloorMin - 2f)
            {
                // (Through a floor is a fall-through; over a lower floor it only stepped off a ledge the line passed over.)
                var through = dwCrossed;
                Problem(d, through ? "fall" : "drop", at, $"{V(dwFloorMin - at.y)} m under the leg's floor, {UnderWhat(source, at)}");
                if (through)
                    dwCounts[key][CFalls]++;
                else
                    dwCounts[key][CDrops]++;
                NextLeg(source, player, true);
                return;
            }
            if (settled && source.IsRock(centre, 0.05f))
            {
                if (dwRockSince < 0f)
                    dwRockSince = Time.time;
                else if (Time.time - dwRockSince > 0.5f)
                {
                    Problem(d, "inrock", at, $"body in rock 0.5 s, cave distance {V(source.CaveDistance(centre))}");
                    dwCounts[key][CFalls]++;
                    NextLeg(source, player, true);
                    return;
                }
            }
            else
                dwRockSince = -1f;
            if (Time.time - dwLegSince > 20f)
            {
                // (A bird that came close and never met rock was circling the point, not held off it.)
                var blockedFrames = PlayerMovementSystem.FlyBlocked.Data - dwFlyBlocked0;
                var circling = dwFlier && blockedFrames == 0 && dwClosest < 4f;
                Problem(d, circling ? "circle" : "timeout", at,
                    $"leg {dwLeg} to {P(target)} not reached in 20 s, {V(math.length(to))} m short, closest {V(dwClosest)} m{(dwFlier ? $", {blockedFrames} rock-blocked frames" : "")}");
                dwCounts[key][circling ? CCircles : CTimeouts]++;
                NextLeg(source, player, true);
                return;
            }
            // Stuck: under 0.3 m of progress in 3 s while pushing (a jump tried at 1.5 s, as a player would).
            if (math.distance(at, dwStillAt) > 0.3f)
            {
                dwStillAt = at;
                dwStillSince = Time.time;
                dwJumped = false;
            }
            else if (Time.time - dwStillSince > 3f)
            {
                Problem(d, "stuck", at, $"{StuckOn(source, at, to)}, going to {P(target)}");
                dwCounts[key][CStuck]++;
                NextLeg(source, player, true);
                return;
            }
            var dir = horizontal > 0.05f ? to.xz / horizontal : float2.zero;
            PlayerInputSystem.TestMove = dir;
            if (dwFlier)
            {
                if (!motion.Flying)
                {
                    PlayerInputSystem.TestJump = motion.Grounded;
                    return;
                }
                var angle = math.atan2(to.y, math.max(horizontal, 0.5f));
                PlayerInputSystem.TestClimb = angle > 0.3f;
                PlayerInputSystem.TestCrouch = angle < -0.3f;
                PlayerInputSystem.TestPitch = math.clamp(angle, -0.3f, 0.3f) / 0.6f;
                // Held off the point for 1.5 s (pressing into a wall it slides along): veer sideways and climb, as a
                // player would, to find the way round.
                if (Time.time - dwStillSince > 1.5f)
                {
                    PlayerInputSystem.TestMove = new float2(-dir.y, dir.x);
                    PlayerInputSystem.TestClimb = true;
                    PlayerInputSystem.TestCrouch = false;
                }
            }
            else if (!dwJumped && Time.time - dwStillSince > 1.5f && motion.Grounded)
            {
                dwJumped = true;
                PlayerInputSystem.TestJump = true;
            }
        }

        void StartWalkDungeon(in TerrainSource source, Entity player)
        {
            var em = queryWorld.EntityManager;
            var d = dwDungeons[dwDungeon];
            var key = $"{d.Type}/{dwBodies[dwBody]}";
            if (!dwCounts.ContainsKey(key))
                dwCounts[key] = new int[CSlots];
            using (var bodyQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>()))
            {
                bodyQuery.TryGetSingleton<PlayerBody>(out var body);
                dwFlier = body.Flies;
                dwSize = body.Size > 0f ? body.Size : 1f;
            }
            dwCounts[key][CDungeons]++;
            // The props, chests and sockets of this dungeon.
            dwProps.Clear();
            foreach (var r in FindObjectsByType<MeshRenderer>())
                if (r.enabled && r.gameObject.name.StartsWith("Dungeon "))
                    dwProps.Add(r.transform);
            dwChests.Clear();
            var sockets = em.GetBuffer<DungeonSocket>(d.Entity, true).ToNativeArray(Allocator.Temp);
            if (d.Type == "Temple")
                foreach (var s in sockets)
                    if (s.Kind == SocketKind.Chest)
                        dwChests.Add((s.Pos + math.mul(s.Rot, new float3(0f, 0.28f, 0f)), s.Rot));
            CheckSockets(source, d, sockets, key);
            dwRoute.Clear();
            dwSlack.Clear();
            if (d.Type == "Temple")
                TempleRoute(source, sockets);
            else
                CaveRoute(source, d, sockets);
            sockets.Dispose();
            if (dwRoute.Count > dwLegCap)
                dwRoute.RemoveRange(dwLegCap, dwRoute.Count - dwLegCap);
            if (dwFlier)
                for (int i = 0; i < dwRoute.Count; i++)
                    dwRoute[i] = WalkMidAir(source, dwRoute[i]);
            WalkLog($"DebugControls: dungeonwalk {dwBodies[dwBody]} {d.Type} {d.Id}: {dwRoute.Count} legs, flies {dwFlier}, size {V(dwSize)}");
            if (dwRoute.Count < 2)
            {
                NextDungeon();
                return;
            }
            dwLeg = 0;
            Teleport(player, dwRoute[0]);
            dwLeg = 1;
            dwPhase = 2;
            dwPhaseSince = Time.time;
        }

        void BeginLeg(in TerrainSource source, float3 at)
        {
            var d = dwDungeons[dwDungeon];
            var key = $"{d.Type}/{dwBodies[dwBody]}";
            dwCounts[key][CLegs]++;
            dwLegSince = dwStillSince = Time.time;
            dwStillAt = at;
            dwJumped = false;
            dwSkipping = false;
            dwHavePrev = dwCrossed = false;
            dwRockSince = -1f;
            dwFlyBlocked0 = PlayerMovementSystem.FlyBlocked.Data;
            dwClosest = float.MaxValue;
            if (!LineClear(source, at, dwRoute[dwLeg], out dwFloorMin, out var why))
            {
                dwCounts[key][CSkipped]++;
                if (dwCounts[key][CSkipped] <= 3)
                    WalkLog($"DebugControls: dungeonwalk skip leg {dwLeg} ({why}) from {P(at)} to {P(dwRoute[dwLeg])}");
                playerQuery.TryGetSingletonEntity<LocalTransform>(out var player);
                Teleport(player, dwRoute[dwLeg]);
                dwLegSince = Time.time;
                dwFloorMin = dwRoute[dwLeg].y;
                // (Counted as skipped, not walked, when the next frame sees it arrived.)
                dwSkipping = true;
            }
        }

        void NextLeg(in TerrainSource source, Entity player, bool teleport)
        {
            if (teleport)
            {
                Teleport(player, dwRoute[dwLeg]);
                dwLeg++;
                if (dwLeg >= dwRoute.Count)
                {
                    NextDungeon();
                    return;
                }
                dwPhase = 2;
                dwPhaseSince = Time.time;
                return;
            }
            dwLeg++;
            if (dwLeg >= dwRoute.Count)
            {
                NextDungeon();
                return;
            }
            BeginLeg(source, queryWorld.EntityManager.GetComponentData<LocalTransform>(player).Position);
        }

        void NextDungeon()
        {
            PlayerInputSystem.TestMove = null;
            PlayerInputSystem.TestPitch = null;
            PlayerInputSystem.TestClimb = PlayerInputSystem.TestCrouch = false;
            dwDungeon++;
            dwPhaseSince = Time.time;
            dwPhase = 1;
            if (dwDungeon < dwDungeons.Count)
                return;
            dwDungeon = 0;
            dwBody++;
            dwPhase = 0;
            if (dwBody < dwBodies.Count)
                return;
            dwOn = false;
            TestHooks.SetBody(dwBodyWas ?? "");
            TestHooks.PutBodyPref(dwPrefWas);
            WalkReport();
        }

        void Teleport(Entity player, float3 p)
        {
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = p + new float3(0f, dwFlier ? 0f : 0.05f, 0f);
            em.SetComponentData(player, t);
            var m = em.GetComponentData<PlayerMotion>(player);
            m.VerticalVelocity = 0f;
            if (dwFlier)
            {
                m.Flying = true;
                m.Airspeed = 1f;
            }
            em.SetComponentData(player, m);
            dwRockSince = -1f;
            dwLegSince = Time.time;
        }

        /// <summary>A cave's route: from its mouth along the passages to each chamber and room (in at the doorway), out to the floor sockets near each and back.</summary>
        void CaveRoute(in TerrainSource source, WalkDungeon d, NativeArray<DungeonSocket> sockets)
        {
            ref var set = ref source.Caves.Value;
            var c = -1;
            for (int i = 0; i < set.Caves.Length && c < 0; i++)
                if (set.Caves[i].Key == d.Id)
                    c = i;
            if (c < 0)
                return;
            ref var cave = ref set.Caves[c];
            int first = cave.FirstNode, count = cave.NodeCount;
            var links = new List<int>[count];
            for (int i = 0; i < count; i++)
                links[i] = new List<int>();
            for (int s = cave.FirstSegment; s < cave.FirstSegment + cave.SegmentCount; s++)
            {
                var seg = set.Segments[s];
                if (seg.A - first < 0 || seg.A - first >= count || seg.B - first < 0 || seg.B - first >= count)
                    continue;
                links[seg.A - first].Add(seg.B - first);
                links[seg.B - first].Add(seg.A - first);
            }
            var src = source;
            float3 Node(int k) => Feet(src, src.Caves.Value.Nodes[first + k].Position);
            var mouthNode = 0;
            for (int k = 1; k < count; k++)
                if (math.distance(set.Nodes[first + k].Position, cave.Mouth) < math.distance(set.Nodes[first + mouthNode].Position, cave.Mouth))
                    mouthNode = k;
            // Rooms by their nearest node.
            var roomAt = new Dictionary<int, int>();
            for (int r = cave.FirstRoom; r < cave.FirstRoom + cave.RoomCount; r++)
            {
                var room = set.Rooms[r];
                var best = 0;
                for (int k = 1; k < count; k++)
                    if (math.distance(set.Nodes[first + k].Position, room.Centre) < math.distance(set.Nodes[first + best].Position, room.Centre))
                        best = k;
                roomAt[best] = r;
            }
            // Targets in the order a walk from the mouth meets them.
            var order = new List<int>();
            var seen = new bool[count];
            var stack = new Stack<int>();
            stack.Push(mouthNode);
            while (stack.Count > 0)
            {
                var k = stack.Pop();
                if (seen[k])
                    continue;
                seen[k] = true;
                if (set.Kinds[first + k] == CaveNodeKind.Chamber || set.Kinds[first + k] == CaveNodeKind.Room || roomAt.ContainsKey(k))
                    order.Add(k);
                foreach (var l in links[k])
                    if (!seen[l])
                        stack.Push(l);
            }
            // In from just outside the mouth.
            var mouthFeet = Feet(source, cave.Mouth);
            var inner = Node(math.min(mouthNode + 1, count - 1));
            var outward = math.normalizesafe(mouthFeet.xz - inner.xz, new float2(0f, 1f));
            var doorstep = new float2(mouthFeet.x, mouthFeet.z) + outward * 3f;
            dwRoute.Add(new float3(doorstep.x, source.FloorUnder(new float3(doorstep.x, mouthFeet.y + 2f, doorstep.y), out _), doorstep.y));
            dwRoute.Add(mouthFeet);
            var used = new bool[sockets.Length];
            var at = mouthNode;
            foreach (var target in order)
            {
                foreach (var k in WalkPath(links, at, target))
                    dwRoute.Add(Node(k));
                at = target;
                var hub = Node(target);
                if (roomAt.TryGetValue(target, out var r))
                {
                    var room = set.Rooms[r];
                    var approach = Feet(source, room.World(new float3(0f, room.RampFloor(-CaveRoom.Wall - 2f) + 0.3f, -CaveRoom.Wall - 2f)));
                    var door = Feet(source, room.World(new float3(0f, 0.3f, -CaveRoom.Wall * 0.5f)));
                    var inside = Feet(source, room.World(new float3(0f, 0.3f, 1f)));
                    var middle = Feet(source, room.World(new float3(0f, 0.3f, room.HZ)));
                    dwRoute.Add(approach);
                    dwRoute.Add(door);
                    dwRoute.Add(inside);
                    dwRoute.Add(middle);
                    var added = 0;
                    for (int i = 0; i < sockets.Length && added < 8; i++)
                    {
                        if (used[i] || !Walkable(sockets[i]))
                            continue;
                        var l = room.Local(sockets[i].Pos);
                        if (math.abs(l.x) > room.HX + 0.3f || l.z < -0.3f || l.z > 2f * room.HZ + 0.3f)
                            continue;
                        used[i] = true;
                        added++;
                        SocketGoal(source, sockets, i);
                        dwRoute.Add(middle);
                    }
                    dwRoute.Add(inside);
                    dwRoute.Add(door);
                    dwRoute.Add(approach);
                    dwRoute.Add(hub);
                    continue;
                }
                var radius = set.Nodes[first + target].Radius + 1f;
                var near = 0;
                for (int i = 0; i < sockets.Length && near < 4; i++)
                {
                    if (used[i] || !Walkable(sockets[i]) || math.distance(sockets[i].Pos, set.Nodes[first + target].Position) > radius)
                        continue;
                    used[i] = true;
                    near++;
                    SocketGoal(source, sockets, i);
                    dwRoute.Add(hub);
                }
            }
            // And back out.
            foreach (var k in WalkPath(links, at, mouthNode))
                dwRoute.Add(Node(k));
            dwRoute.Add(mouthFeet);
        }

        static List<int> WalkPath(List<int>[] links, int from, int to)
        {
            var back = new int[links.Length];
            for (int i = 0; i < back.Length; i++)
                back[i] = -2;
            var queue = new Queue<int>();
            queue.Enqueue(from);
            back[from] = -1;
            while (queue.Count > 0)
            {
                var k = queue.Dequeue();
                if (k == to)
                    break;
                foreach (var l in links[k])
                    if (back[l] == -2)
                    {
                        back[l] = k;
                        queue.Enqueue(l);
                    }
            }
            var path = new List<int>();
            if (back[to] == -2)
                return path;
            for (int k = to; k != from && k >= 0; k = back[k])
                path.Add(k);
            path.Reverse();
            return path;
        }

        /// <summary>The temple's route: room by room through its doorways (depth first, back the way it came), out to each room's floor sockets and back to its middle.</summary>
        // A route leg to socket i: to the thing as the spawner places it (nudged out of a wall or off a chest), and when
        // that has a box, the leg counts as reached at the box's side.
        void SocketGoal(in TerrainSource source, NativeArray<DungeonSocket> sockets, int i)
        {
            var goal = sockets[i].Pos;
            if (sockets[i].Kind != SocketKind.Creature)
            {
                var boxed = Punchy.Fauna.DungeonSpawner.PropPlace(sockets[i], sockets, source, out var mid, out var size, out _);
                goal = new float3(mid.x, sockets[i].Pos.y, mid.z);
                if (boxed)
                {
                    // (The point is on the box's top, not in it: a body set down at the leg's point after a stuck or a
                    // skip would otherwise start inside the box, where nothing holds it and the wall beyond pins it.)
                    dwSlack[dwRoute.Count] = 0.5f * math.length(size.xz) + 0.35f;
                    var feet = Feet(source, goal);
                    dwRoute.Add(new float3(feet.x, math.max(feet.y, mid.y + size.y * 0.5f), feet.z));
                    return;
                }
            }
            dwRoute.Add(Feet(source, goal));
        }

        void TempleRoute(in TerrainSource source, NativeArray<DungeonSocket> sockets)
        {
            if (sockets.Length == 0 || !source.Temple(sockets[0].Pos.xz, out var layout))
                return;
            var src = source;
            ref var rooms = ref source.Landmarks.Value.RuinRooms;
            var n = rooms.Length;
            var links = new List<(int room, float3 door)>[n];
            for (int i = 0; i < n; i++)
                links[i] = new List<(int, float3)>();
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    RuinRoom ra = rooms[a], rb = rooms[b];
                    float ou = math.min(ra.U1, rb.U1) - math.max(ra.U0, rb.U0), ov = math.min(ra.V1, rb.V1) - math.max(ra.V0, rb.V0);
                    if (ou < -0.25f || ov < -0.25f || math.max(ou, ov) < 0.5f)
                        continue;
                    float u = (math.min(ra.U1, rb.U1) + math.max(ra.U0, rb.U0)) * 0.5f, v = (math.min(ra.V1, rb.V1) + math.max(ra.V0, rb.V0)) * 0.5f;
                    var door = Feet(source, SunkenTempleShape.World(layout, new float3(u, math.max(ra.FloorAt(u), rb.FloorAt(u)) + 0.4f, v)));
                    links[a].Add((b, door));
                    links[b].Add((a, door));
                }
            float3 Middle(int i)
            {
                var r = src.Landmarks.Value.RuinRooms[i];
                float u = (r.U0 + r.U1) * 0.5f, v = (r.V0 + r.V1) * 0.5f;
                return Feet(src, SunkenTempleShape.World(layout, new float3(u, r.FloorAt(u) + 0.6f, v)));
            }
            // From the room nearest the first socket.
            var start = 0;
            var q0 = SunkenTempleShape.Local(layout, sockets[0].Pos);
            for (int i = 0; i < n; i++)
                if (rooms[i].Contains(q0.x, q0.z))
                    start = i;
            var used = new bool[sockets.Length];
            var seen = new bool[n];
            void Visit(int i)
            {
                seen[i] = true;
                var middle = Middle(i);
                dwRoute.Add(middle);
                var added = 0;
                for (int s = 0; s < sockets.Length && added < 4; s++)
                {
                    if (used[s] || !Walkable(sockets[s]))
                        continue;
                    var q = SunkenTempleShape.Local(layout, sockets[s].Pos);
                    if (!src.Landmarks.Value.RuinRooms[i].Contains(q.x, q.z))
                        continue;
                    used[s] = true;
                    added++;
                    SocketGoal(src, sockets, s);
                    dwRoute.Add(middle);
                }
                foreach (var (next, door) in links[i])
                    if (!seen[next])
                    {
                        dwRoute.Add(door);
                        Visit(next);
                        dwRoute.Add(door);
                        dwRoute.Add(middle);
                    }
            }
            if (n > 0)
                Visit(start);
        }

        static bool Walkable(in DungeonSocket s) =>
            s.Kind != SocketKind.WallDecor && (s.Tags & (uint)SocketTags.Ceiling) == 0;

        /// <summary>The floor under a point (from half a metre above it).</summary>
        static float3 Feet(in TerrainSource source, float3 p) =>
            new float3(p.x, source.FloorUnder(p + new float3(0f, 0.5f, 0f), out _), p.z);

        static float3 WalkMidAir(in TerrainSource source, float3 feet)
        {
            var air = CeilingAbove(source, feet);
            if (source.TempleAir(feet + new float3(0f, 0.3f, 0f)))
                air = 2.4f;
            return feet + new float3(0f, math.clamp(air * 0.5f, 0.6f, 2f), 0f);
        }

        /// <summary>
        /// Whether a body can walk (or fly) the straight line between two points: every 0.25 m, no rock, kit box or temple
        /// wall at its knees or chest, no step up over 0.6 m and no drop over 3 m. Also the lowest floor met.
        /// </summary>
        bool LineClear(in TerrainSource source, float3 a, float3 b, out float floorMin, out string why)
        {
            floorMin = math.min(a.y, b.y);
            why = "";
            var kit = KitBoxes(out var hasKit);
            var length = math.length((b - a).xz);
            var steps = math.max(1, (int)math.ceil(length / 0.25f));
            var floor = source.FloorUnder(a + new float3(0f, 0.5f, 0f), out _);
            for (int i = 1; i <= steps; i++)
            {
                var p = math.lerp(a, b, i / (float)steps);
                if (dwFlier)
                {
                    // (A body's width clear of rock, not just the line: a bird whose wing grazes a wall slides along it and stalls.)
                    if (PlayerMovementSystem.SolidDistance(source, p) < 0.25f * dwSize || hasKit && InKit(kit, p, out _) || source.TowerWall(p - new float3(0f, 0.3f, 0f), 0.05f, 0.6f * dwSize))
                    {
                        why = "rock or wall on the line";
                        return false;
                    }
                    continue;
                }
                var f = source.FloorUnder(new float3(p.x, floor + 0.7f, p.z), out _);
                if (f > floor + 0.6f)
                {
                    why = $"step up {V(f - floor)} m";
                    return false;
                }
                if (f < floor - 3f)
                {
                    why = $"drop {V(floor - f)} m";
                    return false;
                }
                floor = f;
                floorMin = math.min(floorMin, f);
                var feet = new float3(p.x, f, p.z);
                for (int h = 0; h < 2; h++)
                {
                    var q = feet + new float3(0f, (h == 0 ? 0.5f : 1.2f) * dwSize, 0f);
                    if (source.IsRock(q) || hasKit && InKit(kit, q, out _))
                    {
                        why = h == 0 ? "rock or box at the knees" : "rock or box at the chest";
                        return false;
                    }
                }
                if (source.TowerWall(feet, 0.45f, 1.6f * dwSize))
                {
                    why = "temple wall";
                    return false;
                }
            }
            return true;
        }

        DynamicBuffer<KitBox> KitBoxes(out bool has)
        {
            using var q = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
            has = q.TryGetSingletonBuffer<KitBox>(out var kit, true);
            return kit;
        }

        static bool InKit(in DynamicBuffer<KitBox> kit, float3 p, out int index)
        {
            for (index = 0; index < kit.Length; index++)
            {
                var b = kit[index];
                if (math.distancesq(p, b.Centre) > b.Reach * b.Reach)
                    continue;
                var l = math.mul(math.inverse(b.Rotation), p - b.Centre);
                if (math.all(math.abs(l) <= b.Half))
                    return true;
            }
            index = -1;
            return false;
        }

        static bool InProp(float3 p, out Transform prop)
        {
            foreach (var t in dwProps)
            {
                if (t == null || math.distancesq((float3)t.position, p) > 4f || math.distancesq(((float3)t.position).xz, dwOwnProp) < 0.01f)
                    continue;
                var l = t.InverseTransformPoint(p);
                if (math.abs(l.x) <= 0.5f && math.abs(l.y) <= 0.5f && math.abs(l.z) <= 0.5f)
                {
                    prop = t;
                    return true;
                }
            }
            prop = null;
            return false;
        }

        static bool InChest(float3 p, out int chest)
        {
            for (chest = 0; chest < dwChests.Count; chest++)
            {
                var (c, r) = dwChests[chest];
                // (A chest socket's own chest doesn't count: it may sit a little off its socket.)
                if (dwOwnIsChest && math.distancesq(c.xz, dwOwnProp) < 0.36f)
                    continue;
                var l = math.mul(math.inverse(r), p - c);
                if (math.all(math.abs(l) <= new float3(0.41f, 0.28f, 0.26f)))
                    return true;
            }
            chest = -1;
            return false;
        }

        /// <summary>What a point is inside, if anything: "" for open air.</summary>
        string InsideWhat(in TerrainSource source, float3 p, bool rock)
        {
            var kit = KitBoxes(out var hasKit);
            if (hasKit && InKit(kit, p, out var k))
                return $"kit box {k} at {P(kit[k].Centre)} half {P(kit[k].Half)}";
            if (InProp(p, out var prop))
                return $"prop '{prop.name}' at {P(prop.position)} size {P(prop.localScale)}";
            if (InChest(p, out var chest))
                return $"chest {chest} at {P(dwChests[chest].c)}";
            if (source.Temple(p.xz, out var layout) && source.TempleAir(p) == false && p.y < source.SampleHeight(p.xz))
            {
                var q = SunkenTempleShape.Local(layout, p);
                if (SunkenTempleShape.Solid(ref source.Landmarks.Value, q.x, q.y, q.z, source.SampleHeight(p.xz) - layout.Base))
                    return "temple solid";
            }
            if (rock && source.IsRock(p, 0.05f))
                return $"rock (cave distance {V(source.CaveDistance(p))})";
            return "";
        }

        void CheckClips(in TerrainSource source, WalkDungeon d, float3 at)
        {
            // The body's middle (rock is the fall-through check's), then the camera.
            var centre = at + new float3(0f, 0.6f * dwSize, 0f);
            var what = InsideWhat(source, centre, false);
            if (what.Length > 0 && Problem(d, "bodyclip", at, what))
                dwCounts[$"{d.Type}/{dwBodies[dwBody]}"][CBodyClips]++;
            var cam = Camera.main;
            if (cam == null)
                return;
            float3 eye = cam.transform.position;
            what = InsideWhat(source, eye, true);
            if (what.Length > 0 && Problem(d, "camclip", eye, what))
                dwCounts[$"{d.Type}/{dwBodies[dwBody]}"][CCamClips]++;
        }

        /// <summary>Sockets whose thing would stand in a wall: the middle (logged), or a corner of its box (counted).</summary>
        void CheckSockets(in TerrainSource source, WalkDungeon d, NativeArray<DungeonSocket> sockets, string key)
        {
            // (Once per dungeon: the first body's pass.)
            if (dwBody > 0)
                return;
            foreach (var s in sockets)
            {
                if (!Walkable(s))
                    continue;
                // (A-95: the thing's box as the spawner places it, nudged off a chest or out of a wall; a creature's
                // a body-sized box on the socket.)
                float3 mid, size;
                quaternion rot = s.Rot;
                var placed = true;
                if (s.Kind == SocketKind.Creature)
                {
                    size = new float3(0.6f, 0.8f, 0.6f);
                    mid = s.Pos + new float3(0f, size.y * 0.5f, 0f);
                }
                else
                    placed = Punchy.Fauna.DungeonSpawner.PropPlace(s, sockets, source, out mid, out size, out rot);
                // (Its own stand-in prop doesn't count.)
                dwOwnProp = mid.xz;
                dwOwnIsChest = s.Kind == SocketKind.Chest;
                var what = placed ? InsideWhat(source, mid, true) : "no clear spot within the nudge";
                if (what.Length > 0)
                {
                    Problem(d, "socket", s.Pos, $"{s.Kind} socket (tags 0x{s.Tags:x}, size {s.Size}) box middle {P(mid)} in {what}");
                    dwCounts[key][CSockets]++;
                    continue;
                }
                for (int c = 0; c < 8; c++)
                {
                    var corner = new float3((c & 1) == 0 ? -0.45f : 0.45f, (c & 2) == 0 ? -0.4f : 0.45f, (c & 4) == 0 ? -0.45f : 0.45f) * size;
                    if (InsideWhat(source, mid + math.mul(rot, corner), true).Length > 0)
                    {
                        dwCounts[key][CPokes]++;
                        break;
                    }
                }
            }
            dwOwnProp = new float2(1e9f, 1e9f);
            dwOwnIsChest = false;
        }

        /// <summary>Logs a problem the first time it's met (per dungeon, body, kind and thing); counts frames after. True when new.</summary>
        bool Problem(WalkDungeon d, string kind, float3 at, string near)
        {
            var thing = near.Split(new[] { " at ", " (" }, System.StringSplitOptions.None)[0];
            var cell = kind == "bodyclip" || kind == "camclip" ? $"{thing}|{(int)math.floor(at.x / 2f)},{(int)math.floor(at.z / 2f)}" : P(at);
            var id = $"{d.Id}|{dwBodies[dwBody]}|{kind}|{cell}";
            if (dwProblemIndex.TryGetValue(id, out var i))
            {
                var p = dwProblems[i];
                p.Frames++;
                dwProblems[i] = p;
                return false;
            }
            dwProblemIndex[id] = dwProblems.Count;
            dwProblems.Add(new WalkProblem { Type = d.Type, Body = dwBodies[dwBody], Kind = kind, Near = near, At = at, Dungeon = (int)d.Id, Frames = 1 });
            WalkLog($"DebugControls: dungeonwalk PROBLEM type={d.Type} dungeon={d.Id} body={dwBodies[dwBody]} kind={kind} leg={dwLeg} at {P(at)} near={near}");
            return true;
        }

        string StuckOn(in TerrainSource source, float3 at, float3 to)
        {
            var ahead = at + new float3(math.normalizesafe(to.xz).x * 0.4f, 0f, math.normalizesafe(to.xz).y * 0.4f);
            for (int h = 0; h < 4; h++)
            {
                var q = ahead + new float3(0f, (0.2f + 0.45f * h) * dwSize, 0f);
                var what = InsideWhat(source, q, true);
                if (what.Length > 0)
                    return $"blocked {V(0.2f + 0.45f * h)} m up by {what}";
            }
            var floor = source.FloorUnder(ahead + new float3(0f, 1f, 0f), out _);
            if (floor > at.y + 0.4f)
                return $"floor ahead {V(floor - at.y)} m higher";
            if (source.TowerWall(ahead, 0.45f, 1.6f * dwSize))
                return "temple wall ahead";
            return $"nothing found ahead, {V(math.length(to))} m to go";
        }

        string UnderWhat(in TerrainSource source, float3 at)
        {
            var floor = source.FloorUnder(at + new float3(0f, 3f, 0f), out var inCave);
            return $"floor above {V(floor - at.y)} m, in cave {inCave}, ground {V(source.SampleHeight(at.xz) - at.y)} m above";
        }

        /// <summary>Logs a walk line, and in the editor also appends it to Temp/dungeonwalk.log (several editors share one Editor.log).</summary>
        static void WalkLog(string line)
        {
            Debug.Log(line);
#if UNITY_EDITOR
            System.IO.File.AppendAllText("Temp/dungeonwalk.log", line + System.Environment.NewLine);
#endif
        }

#if UNITY_EDITOR
        // Play mode may start without a domain reload: an aborted walk must not carry on into the next run.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetWalk()
        {
            dwPending = null;
            dwOn = false;
        }
#endif

        static string P(float3 p) => $"({V(p.x)}, {V(p.y)}, {V(p.z)})";

        void WalkReport()
        {
            var sb = new StringBuilder();
            foreach (var type in new[] { "Cave", "Ruin", "Temple" })
            {
                var total = new int[CSlots];
                foreach (var body in dwBodies)
                {
                    if (!dwCounts.TryGetValue($"{type}/{body}", out var c))
                        continue;
                    for (int i = 0; i < CSlots; i++)
                        total[i] += c[i];
                    WalkLog($"DebugControls: dungeonwalk TABLE type={type} body={body} dungeons={c[CDungeons]} legs={c[CLegs]} walked={c[CWalked]} " +
                              $"skipped={c[CSkipped]} falls={c[CFalls]} stuck={c[CStuck]} timeouts={c[CTimeouts]} drops={c[CDrops]} circles={c[CCircles]} bodyclips={c[CBodyClips]} " +
                              $"camclips={c[CCamClips]} sockets_in_wall={c[CSockets]} socket_boxes_poking={c[CPokes]}");
                }
                sb.Append($" {type}: falls {total[CFalls]}, stuck {total[CStuck]}, timeouts {total[CTimeouts]}, ledge drops {total[CDrops]}, body clips {total[CBodyClips]}, " +
                          $"camera clips {total[CCamClips]}, sockets in walls {total[CSockets]};");
            }
            var worst = new List<WalkProblem>(dwProblems);
            worst.Sort((a, b) => a.Severity != b.Severity ? b.Severity.CompareTo(a.Severity) : b.Frames.CompareTo(a.Frames));
            for (int i = 0; i < math.min(10, worst.Count); i++)
            {
                var w = worst[i];
                WalkLog($"DebugControls: dungeonwalk WORST {i + 1}: type={w.Type} dungeon={w.Dungeon} body={w.Body} kind={w.Kind} frames={w.Frames} at {P(w.At)} near={w.Near}");
            }
            WalkLog($"DebugControls: dungeonwalk DONE {dwDungeons.Count} dungeons x {dwBodies.Count} bodies, {dwProblems.Count} problems:{sb}");
        }
    }
}
