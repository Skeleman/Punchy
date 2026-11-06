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
    // DebugControls, fauna shots: hunts, chats, colossi, mantraps and the ocotillo mimic. Code moved from DebugControls.cs (W-116); the class summary is there.
    public partial class DebugControls
    {
        // ---------------------------------------------------------------- A-09 shots

        static bool shotOnHunt;
        static string huntSpecies;

        /// <summary>"-gaitlog" (A-21, #6): each four-legged species among the loaded creatures and its gallop.</summary>
        void GaitLog()
        {
            if (queryWorld == null || !queryWorld.IsCreated)
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            using var q = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureBody>());
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var body in bodies)
            {
                ref var b = ref body.Blob.Value;
                if (!b.Gallops || !seen.Add(body.Species))
                    continue;
                Debug.Log($"DebugControls: gait {director?.SpeciesAt(body.Species)?.name}: {(b.LeapingRun ? "Leap" : b.Bounds ? "Bound" : "Rotary")}");
            }
            bodies.Dispose();
        }
        bool huntShotTaken;
        static float colossusShotDistance;
        static string mantrapShotMode;
        static bool mantrapLungePending;

        /// <summary>
        /// "-shotonhunt" on a shot-list line: the moment a land hunter's chase turns to a run, the camera is put 16 m to the
        /// side of the hunter and its nearest prey and the line's shot is brought forward to 0.3 s later (#9). The line's -wait
        /// is the longest it waits for a hunt.
        /// </summary>
        void ShotOnHunt()
        {
            if (!shotOnHunt || shotIndex < 0 || shotStarted < 0f || queryWorld == null || !queryWorld.IsCreated)
                return;
            if (huntShotTaken)
            {
                FollowHunter();
                return;
            }
            var em = queryWorld.EntityManager;
            // (A-23: the last line's hunter is skipped until its sprint ends, or a back-to-back line caught it slowing down
            // and shot it walking.)
            if (staleHunter != Entity.Null && (!em.Exists(staleHunter) || !em.HasComponent<CreatureState>(staleHunter) ||
                                               em.GetComponentData<CreatureState>(staleHunter) is var st && (st.Mode != CreatureMode.Hunt || st.Stage != 1)))
                staleHunter = Entity.Null;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < states.Length; i++)
            {
                var h = states[i];
                if (h.Mode != CreatureMode.Hunt || h.Stage != 1 || entities[i] == staleHunter || bodies[i].Blob.Value.Habitat == Habitat.Flies)
                    continue;
                if (huntSpecies != null && FindAnyObjectByType<FaunaDirector>()?.SpeciesAt(bodies[i].Species)?.name != huntSpecies)
                    continue;
                var prey = -1;
                var best = 60f;
                for (int j = 0; j < states.Length; j++)
                {
                    if (j == i || bodies[j].Species == bodies[i].Species)
                        continue;
                    var d = math.distance(states[j].Position, h.Position);
                    if (d < best) { best = d; prey = j; }
                }
                if (prey < 0)
                    continue;
                var p = states[prey].Position;
                var mid = (h.Position + p) * 0.5f;
                var along = math.normalizesafe(p - h.Position, new float3(0f, 0f, 1f));
                var side = math.normalizesafe(new float3(-along.z, 0f, along.x));
                var from = mid + side * (16f + best * 0.5f) + new float3(0f, 3.5f, 0f);
                PoseCamera(from, Quaternion.LookRotation((Vector3)(mid + new float3(0f, 0.8f, 0f)) - (Vector3)from));
                huntShotTaken = true;
                shotWait = Time.realtimeSinceStartup - shotStarted + (huntSide > 0f ? huntDelay : 0.3f);
                huntHunter = entities[i];
                huntPrey = entities[prey];
                huntSideDir = side;
                huntShotLine = shotIndex;
                if (huntSide > 0f)
                    FollowHunter();
                var director = FindAnyObjectByType<FaunaDirector>();
                Debug.Log($"DebugControls: shotonhunt: {director?.SpeciesAt(bodies[i].Species)?.name} sprinting at {director?.SpeciesAt(bodies[prey].Species)?.name}, {best:F1} m apart, hunter speed {h.Speed:F1}, at ({h.Position.x:F0}, {h.Position.z:F0})");
                break;
            }
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
        }

        // "-huntside 7" with -shotonhunt (#6): instead of the wide two-animal view, the camera rides that many metres
        // side-on of the hunter, and the shot is taken huntDelay seconds into the sprint (so the gallop has started).
        static float huntSide;
        static bool huntPair;
        const float huntDelay = 1f;
        Entity huntHunter, huntPrey, staleHunter;
        float3 huntSideDir;
        int huntShotLine = -1;
        bool huntShotLogged;

        void FollowHunter()
        {
            if (huntSide <= 0f || shotIndex != huntShotLine || huntHunter == Entity.Null)
                return;
            var em = queryWorld.EntityManager;
            if (!em.Exists(huntHunter) || !em.HasComponent<CreatureState>(huntHunter))
                return;
            var h = em.GetComponentData<CreatureState>(huntHunter);
            // Side-on to the hunter's own heading this frame (A-23: the hunter-to-prey line drifted off the heading as the
            // wolf curved after the hare, and the A-22 shot came out head-on), keeping the side picked at the start.
            var along = new float3(math.sin(h.Yaw), 0f, math.cos(h.Yaw));
            var side = math.normalizesafe(new float3(-along.z, 0f, along.x));
            if (math.dot(side, huntSideDir) < 0f)
                side = -side;
            var target = h.Position + new float3(0f, 0.6f, 0f);
            var distance = huntSide;
            var gap = 0f;
            if (huntPair && em.Exists(huntPrey) && em.HasComponent<CreatureState>(huntPrey))
            {
                var p = em.GetComponentData<CreatureState>(huntPrey).Position;
                gap = math.distance(p.xz, h.Position.xz);
                target = (h.Position + p) * 0.5f + new float3(0f, 0.5f, 0f);
                // Step back if the pair is wider than the view holds at this distance (about 1.6 d across).
                distance = math.max(huntSide, gap * 0.65f);
            }
            // (A-18: keep the side with a clear view. A shrub or tree between the camera and the hunter hid half the
            // wolf in A-17, so if this side has plants in the way and the other has fewer, swap over.)
            var blocked = SightBlockers(target, target + side * distance + new float3(0f, 0.9f, 0f));
            var otherBlocked = blocked > 0 ? SightBlockers(target, target - side * distance + new float3(0f, 0.9f, 0f)) : 0;
            if (blocked > 0 && otherBlocked < blocked)
            {
                side = -side;
                (blocked, otherBlocked) = (otherBlocked, blocked);
            }
            huntSideDir = side;
            var from = target + side * distance + new float3(0f, 0.9f, 0f);
            PoseCamera(from, Quaternion.LookRotation((Vector3)(target - from)));
            var elapsed = Time.realtimeSinceStartup - shotStarted;
            if (!huntShotLogged && elapsed >= shotWait - 0.05f)
            {
                huntShotLogged = true;
                Debug.Log($"DebugControls: huntside: hunter speed {h.Speed:F1}, mode {h.Mode} stage {h.Stage}, camera {distance:F1} m side-on of the heading{(huntPair ? $" (pair, {gap:F1} m apart)" : "")}, " +
                          $"prey line {PreyLineAngle(h, along):F0} deg off the heading, " +
                          $"{elapsed:F2} s into the line, plants in the way {blocked}, leap gather {CreatureAnimator.LeapGather(h.Phase):F2}");
            }
        }

        /// <summary>How far the hunter-to-prey line is turned from the hunter's heading, degrees (-1 with no prey).</summary>
        float PreyLineAngle(CreatureState h, float3 along)
        {
            var em = queryWorld.EntityManager;
            if (!em.Exists(huntPrey) || !em.HasComponent<CreatureState>(huntPrey))
                return -1f;
            var line = em.GetComponentData<CreatureState>(huntPrey).Position - h.Position;
            line.y = 0f;
            return math.degrees(math.acos(math.clamp(math.dot(math.normalizesafe(line), along), -1f, 1f)));
        }

        /// <summary>
        /// Shrubs and trees (and rocks) standing between two points: placed plants in the shrub and tree layers taller than
        /// half a metre whose footprint or crown comes within reach of the line on the ground (FloraPlacement.Place, the
        /// same pure function FloraSystem draws from). -1 if the flora tables aren't up yet.
        /// </summary>
        int SightBlockers(float3 a, float3 b)
        {
            if (!EnsureLocations(out var land))
                return -1;
            using var q = queryWorld.EntityManager.CreateEntityQuery(typeof(Punchy.Flora.FloraLookup));
            if (!q.TryGetSingleton<Punchy.Flora.FloraLookup>(out var lookup) || !lookup.Blob.IsCreated)
                return -1;
            ref var f = ref lookup.Blob.Value;
            var count = 0;
            var lo = math.min(a.xz, b.xz) - 4f;
            var hi = math.max(a.xz, b.xz) + 4f;
            for (int layer = 1; layer <= Punchy.Flora.FloraPlacement.TreeLayer; layer++)
            {
                var size = f.CellSizes[layer];
                var c0 = (int2)math.floor(lo / size);
                var c1 = (int2)math.floor(hi / size);
                for (int y = c0.y; y <= c1.y; y++)
                for (int x = c0.x; x <= c1.x; x++)
                {
                    var plant = Punchy.Flora.FloraPlacement.Place(ref f, land, new int2(x, y), layer);
                    if (plant.Model < 0 || plant.Model >= f.Models.Length)
                        continue;
                    var shape = f.Models[plant.Model];
                    if (shape.Height * plant.Scale < 0.5f)
                        continue;
                    // (Its distance from the line on the ground, the ends left out: something right behind the hunter
                    // or the camera doesn't hide the body.)
                    var ab = b.xz - a.xz;
                    var t = math.saturate(math.dot(plant.Position.xz - a.xz, ab) / math.max(math.lengthsq(ab), 1e-4f));
                    if (t < 0.08f || t > 0.95f)
                        continue;
                    // (A tree whose crown starts above the camera only gets in the way with its trunk.)
                    var crown = shape.CrownBase * plant.Scale > 1.8f ? 0f : shape.CrownRadius;
                    var reach = math.max(math.max(crown, shape.TrunkRadius), 0.6f) * plant.Scale + 0.3f;
                    if (math.distance(plant.Position.xz, a.xz + ab * t) < reach)
                        count++;
                }
            }
            return count;
        }

        static bool remoteRoll;
        int remoteRollStep;

        // A-26: "-chatshot" on a shot-list line: the first two folk stood talking (the one who started it and the one it
        // stopped, within 1.8 m of each other) once the line has run 2 s: the camera 4 m side-on of the pair at head
        // height, looking at the middle of them, and the line's shot brought forward to 1.2 s later (their gestures and
        // turned heads settled). The line's -wait is the longest it waits for a talk.
        static bool chatShot;
        Entity chatA, chatB;
        float3 chatSide;
        int chatShotLine = -1;

        void ChatShot()
        {
            if (!chatShot || shotIndex < 0 || shotStarted < 0f || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            if (chatShotLine == shotIndex)
            {
                // (Held on the pair until the shot.)
                if (em.Exists(chatA) && em.Exists(chatB))
                    FrameChat(em.GetComponentData<CreatureState>(chatA).Position, em.GetComponentData<CreatureState>(chatB).Position, false);
                return;
            }
            if (Time.realtimeSinceStartup - shotStarted < 2f)
                return;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var entities = q.ToEntityArray(Allocator.Temp);
            var eye = Camera.main != null ? (float3)Camera.main.transform.position : float3.zero;
            var best = -1;
            var bestPartner = -1;
            var bestD = float.MaxValue;
            for (int i = 0; i < states.Length; i++)
            {
                var a = states[i];
                if (a.ChatRole != 1 || a.Mode != CreatureMode.Idle || a.Stage != CreatureAnimator.ChatTalk && a.Stage != CreatureAnimator.ChatListen)
                    continue;
                for (int j = 0; j < states.Length; j++)
                    if (entities[j] == a.Partner && states[j].ChatRole == 2 && states[j].Partner == entities[i] &&
                        math.distance(states[j].Position.xz, a.Position.xz) < 1.8f && states[j].Speed < 0.2f && a.Speed < 0.2f)
                    {
                        var d = math.distancesq(a.Position, eye);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = i;
                            bestPartner = j;
                        }
                    }
            }
            if (best >= 0)
            {
                chatA = entities[best];
                chatB = entities[bestPartner];
                chatShotLine = shotIndex;
                FrameChat(states[best].Position, states[bestPartner].Position, true);
                shotWait = Time.realtimeSinceStartup - shotStarted + 1.2f;
                var director = FindAnyObjectByType<FaunaDirector>();
                Debug.Log($"DebugControls: chatshot: {director?.SpeciesAt(em.GetComponentData<CreatureBody>(chatA).Species)?.name} and " +
                          $"{director?.SpeciesAt(em.GetComponentData<CreatureBody>(chatB).Species)?.name} talking, " +
                          $"{math.distance(states[best].Position.xz, states[bestPartner].Position.xz):F1} m apart, at ({states[best].Position.x:F0}, {states[best].Position.z:F0}), " +
                          $"camera 4 m side-on ({SightBlockers((states[best].Position + states[bestPartner].Position) * 0.5f + new float3(0f, 1.2f, 0f), (states[best].Position + states[bestPartner].Position) * 0.5f + chatSide * 4f + new float3(0f, 1.4f, 0f))} plants in the way)");
            }
            states.Dispose();
            entities.Dispose();
        }

        void FrameChat(float3 a, float3 b, bool pickSide)
        {
            var mid = (a + b) * 0.5f;
            var along = math.normalizesafe(new float3(b.x - a.x, 0f, b.z - a.z), new float3(1f, 0f, 0f));
            var side = new float3(-along.z, 0f, along.x);
            if (pickSide)
            {
                // (The side with fewer plants between the camera and the pair.)
                var target = mid + new float3(0f, 1.2f, 0f);
                if (SightBlockers(target, target + side * 4f + new float3(0f, 0.2f, 0f)) > SightBlockers(target, target - side * 4f + new float3(0f, 0.2f, 0f)))
                    side = -side;
                chatSide = side;
            }
            else if (math.dot(side, chatSide) < 0f)
                side = -side;
            var look = mid + new float3(0f, 1.15f, 0f);
            var from = mid + chatSide * 4f + new float3(0f, 1.45f, 0f);
            PoseCamera(from, Quaternion.LookRotation((Vector3)(look - from)));
        }

        /// <summary>
        /// "-remoteroll" on a shot-list line (a joined client, #48): the first time another player's body starts its
        /// somersault (Roll above 0, from the Pose byte), the camera is put 5 m to its side and a log line written; once
        /// Roll passes 0.4 a shot (remoteroll.png, into -shotdir) and a second line with the roll value.
        /// </summary>
        void RemoteRoll()
        {
            if (!remoteRoll || remoteRollStep >= 2 || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<RemoteAvatar>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            for (int i = 0; i < states.Length; i++)
            {
                var r = states[i];
                if (r.Roll <= 0f)
                    continue;
                if (remoteRollStep == 0)
                {
                    var fwd = new float3(math.sin(r.Yaw), 0f, math.cos(r.Yaw));
                    var from = r.Position + new float3(fwd.z, 0f, -fwd.x) * 5f + new float3(0f, 1.2f, 0f);
                    PoseCamera(from, Quaternion.LookRotation((Vector3)(r.Position + new float3(0f, 0.5f, 0f)) - (Vector3)from));
                    remoteRollStep = 1;
                    Debug.Log($"DebugControls: remoteroll: remote player rolling (Roll {r.Roll:F2}) at ({r.Position.x:F1}, {r.Position.z:F1})");
                }
                else if (r.Roll > 0.4f)
                {
                    if (shotDir != null)
                        Shot("remoteroll.png");
                    remoteRollStep = 2;
                    Debug.Log($"DebugControls: remoteroll: shot at Roll {r.Roll:F2}");
                }
                break;
            }
            states.Dispose();
        }

        /// <summary>"-colossusshot 300": the player and camera that far from the Dunes colossus (south-west of it), looking at its chest.</summary>
        void ColossusShot()
        {
            if (colossusShotDistance <= 0f || !DunesColossus.Placed || !EnsureLocations(out var ground) ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var d = colossusShotDistance;
            colossusShotDistance = 0f;
            var at = DunesColossus.Position;
            var stand = at.xz + math.normalize(new float2(-0.6f, -0.8f)) * d;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(stand.x, ground.SampleHeight(stand) + 0.1f, stand.y);
            em.SetComponentData(player, t);
            var eye = new Vector3(stand.x, ground.SampleHeight(stand) + (d > 150f ? 25f : 4f), stand.y);
            var target = new Vector3(at.x, at.y + (d > 150f ? 33f : 38f), at.z);
            PoseCamera(eye, Quaternion.LookRotation(target - eye));
            if (ground.Settlements.IsCreated)
            {
                // A-12 (#40): how far the colossus stands from the nearest settlement.
                ref var towns = ref ground.Settlements.Value;
                var near = float.MaxValue;
                var nearAt = float2.zero;
                for (int k = 0; k < towns.Settlements.Length; k++)
                {
                    var dist = math.distance(towns.Settlements[k].Centre, at.xz);
                    if (dist < near)
                    {
                        near = dist;
                        nearAt = towns.Settlements[k].Centre;
                    }
                }
                Debug.Log($"DebugControls: colossus at ({at.x:F0}, {at.z:F0}): nearest settlement {near:F0} m away at ({nearAt.x:F0}, {nearAt.y:F0}), of {towns.Settlements.Length}");
            }
            Debug.Log($"DebugControls: colossus shot from {d:F0} m: camera ({eye.x:F0}, {eye.y:F0}, {eye.z:F0}) looking at ({target.x:F0}, {target.y:F0}, {target.z:F0})");
        }

        /// <summary>
        /// A-72: "-walkershot Saguaro,80[,side|front|back]": the camera follows that walking colossus from that far off
        /// (ColossusWalker.ShotPose), with the player moved to stand there when more than 40 m away, so the ground streams in.
        /// </summary>
        void WalkerShot()
        {
            if (ColossusWalker.Shot == null || !ColossusWalker.ShotPose(ColossusWalker.Shot, out var eye, out var rotation, out var stand))
                return;
            PoseCamera(eye, rotation);
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            if (math.distance(t.Position.xz, stand.xz) <= 40f)
                return;
            t.Position = stand;
            em.SetComponentData(player, t);
        }

        /// <summary>
        /// "-mantrapshot side": the player 5 m south of the first carnivorous plant and the camera 4 m east of it, looking at its
        /// head side-on. "-mantrapshot lunge": the player 0.6 m from it, the camera 3 m east, and the strike held at its peak.
        /// </summary>
        void MantrapShot()
        {
            // A-10c: the lunge shot is taken 0.15 s into the 0.3 s strike hold (the line's -wait is the longest it waits).
            if (mantrapLungePending && Mantrap.StrikeHeld && shotIndex >= 0 && shotStarted >= 0f)
            {
                mantrapLungePending = false;
                shotWait = Time.realtimeSinceStartup - shotStarted + 0.15f;
                Debug.Log($"DebugControls: mantrap strike held, shot in 0.15 s (hold {Mantrap.HoldTime:F2} s)");
            }
            if (mantrapShotMode == null || !Mantrap.Placed || Mantrap.Spots.Count == 0 || !EnsureLocations(out var ground) ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var lunge = mantrapShotMode == "lunge";
            mantrapShotMode = null;
            Mantrap.HoldStrike = lunge;
            Mantrap.StrikeHeld = false;
            mantrapLungePending = lunge;
            Mantrap.ThinFog = true;
            var at = Mantrap.Spot(0);
            var stand = new float2(at.x, at.z - (lunge ? 0.6f : 5f));   // player south of the plant; camera east, level with the head
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(stand.x, ground.SampleHeight(stand) + 0.1f, stand.y);
            em.SetComponentData(player, t);
            var cam = new float2(at.x + 4f, at.z - (lunge ? 0.3f : 0f));
            var eye = new Vector3(cam.x, at.y + 2.5f, cam.y);
            var target = new Vector3(at.x, at.y + (lunge ? 1.2f : 1.6f), at.z - (lunge ? 0.3f : 0f));
            PoseCamera(eye, Quaternion.LookRotation(target - eye));
            Debug.Log($"DebugControls: mantrap {(lunge ? "lunge" : "side")} shot at plant ({at.x:F0}, {at.z:F0}): camera {Vector3.Distance(eye, new Vector3(at.x, eye.y, at.z)):F1} m away");
        }

        // ---------------------------------------------------------------- A-11 ocotillo mimic

        /// <summary>"-mimicshot rest|run" (with "-spawn OcotilloMimic:1 -place tree:Ocotillo"): "rest" stands the mimic 2.5 m
        /// from the lone ocotillo and frames both side-on; "run" walks the player at it from 30 m, logs the startle distance,
        /// fold time, run speed and settle time, and takes the shot mid-run.</summary>
        static string mimicShotMode;
        static bool mimicTreeSet;
        static float3 mimicTree;
        static float mimicTreeHeight;
        Entity mimicEntity;
        float mimicT0 = -1f, mimicLogAt, mimicStartT = -1f, mimicEndT = -1f, mimicMaxSpeed, mimicShotT = -1f;
        float3 mimicStartPos, mimicRunPos, mimicEndPos;
        float mimicRunT;
        bool mimicFolded, mimicSettled, mimicRunMarked;

        /// <summary>Real ocotillo (flora recipe) vs the mimic's arms, side by side in the log.</summary>
        void LogMimicVsReal()
        {
            FloraSpecies real = null;
            CreatureSpecies mimic = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<FloraSpecies>())
                if (f.name == "Ocotillo")
                    real = f;
            foreach (var c in Resources.FindObjectsOfTypeAll<CreatureSpecies>())
                if (c.name == "OcotilloMimic")
                    mimic = c;
            if (real == null || mimic == null)
            {
                Debug.Log($"DebugControls: mimic vs real: assets not found (real {real != null}, mimic {mimic != null})");
                return;
            }
            // Recipe: stems Branches (random), tilt 4..28 deg off vertical (mean 16), length 0.7..1 of the height.
            var arms = 0;
            float sum = 0f, lo = 999f, hi = 0f;
            foreach (var p in mimic.Parts)
            {
                var tilt = 90f + p.Direction.x;
                var n = p.Mirror ? 2 : 1;
                arms += n;
                sum += tilt * n;
                lo = Mathf.Min(lo, tilt);
                hi = Mathf.Max(hi, tilt);
            }
            var size = (mimic.Size.x + mimic.Size.y) * 0.5f;
            Debug.Log($"DebugControls: REAL ocotillo: stems {real.Branches.x:F0}-{real.Branches.y:F0}, splay 4-28 deg (mean 16), height {real.Height.x:F1}-{real.Height.y:F1} m");
            Debug.Log($"DebugControls: MIMIC: arms {arms}, splay {lo:F0}-{hi:F0} deg (mean {sum / math.max(arms, 1):F0}), arm tilt from Direction only; run speed {mimic.RunSpeed:F1} x scale {size:F1}, flee distance {mimic.FleeDistance:F1} x scale");
        }

        void MimicShot()
        {
            if (mimicShotMode == null || !mimicTreeSet || !EnsureLocations(out var ground) ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            var index = director != null ? director.SpeciesIndex("OcotilloMimic") : -1;
            if (index < 0)
                return;
            var em = queryWorld.EntityManager;
            var now = Time.realtimeSinceStartup;
            if (mimicT0 < 0f)
            {
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                var entities = q.ToEntityArray(Allocator.Temp);
                var pp = em.GetComponentData<LocalTransform>(player).Position;
                var best = float.MaxValue;
                mimicEntity = Entity.Null;
                foreach (var e in entities)
                {
                    if (em.GetComponentData<CreatureBody>(e).Species != index)
                        continue;
                    var d = math.distance(em.GetComponentData<CreatureState>(e).Position, pp);
                    if (d < best)
                    {
                        best = d;
                        mimicEntity = e;
                    }
                }
                entities.Dispose();
                if (mimicEntity == Entity.Null)
                    return;
                var run = mimicShotMode == "run";
                var at = mimicTree;
                var spot = run ? new float2(at.x - 12f, at.z) : new float2(at.x - 2.5f, at.z);
                var s = em.GetComponentData<CreatureState>(mimicEntity);
                s.Position = new float3(spot.x, ground.SampleHeight(spot), spot.y);
                s.Home = spot;
                s.Mode = CreatureMode.Rest;
                s.Timer = 600f;
                s.Speed = 0f;
                var settle = mimicShotMode == "settle";
                s.Curl = settle ? 0f : 1f;
                s.Lie = settle ? 0f : 1f;
                em.SetComponentData(mimicEntity, s);
                mimicT0 = now;
                if (mimicTreeHeight < 0.5f)
                    mimicTreeHeight = 3.5f; // the flora blob reads 0 for this tree: frame a fixed 3.5 m
                LogMimicVsReal();
                mimicLogAt = 0f;
                Debug.Log($"DebugControls: mimic {mimicShotMode}: placed at ({spot.x:F1}, {spot.y:F1}), tree ({at.x:F1}, {at.z:F1}) {mimicTreeHeight:F1} m tall");
            }
            if (!em.Exists(mimicEntity))
                return;
            var m = em.GetComponentData<CreatureState>(mimicEntity);
            var pt = em.GetComponentData<LocalTransform>(player);
            if (mimicShotMode != "run")
            {
                if (mimicShotMode == "settle" && !mimicSettled && m.Curl >= 0.95f)
                {
                    mimicSettled = true;
                    Debug.Log($"DebugControls: mimic SETTLE (safe, Rest, arms down) to arms fully up (curl >= 0.95) took {now - mimicT0:F2} s");
                }
                var far = new float2(m.Position.x + 60f, m.Position.z);
                pt.Position = new float3(far.x, ground.SampleHeight(far) + 0.1f, far.y);
                em.SetComponentData(player, pt);
                var mid = new Vector3(mimicTree.x - 1.25f, mimicTree.y, mimicTree.z);
                var dist = 5f; // A-12: about 5 m back, so both plants fill the frame
                var eye = mid + new Vector3(0f, mimicTreeHeight * 0.5f, -dist);
                PoseCamera(eye, Quaternion.LookRotation(mid + Vector3.up * mimicTreeHeight * 0.5f - eye));
                if (now - mimicT0 > 3f && now - mimicLogAt > 1f)
                {
                    mimicLogAt = now;
                    Debug.Log($"DebugControls: mimic rest: mode {m.Mode}, curl {m.Curl:F2}, lie {m.Lie:F2}, speed {m.Speed:F2}");
                }
                return;
            }

            // Run: until the startle the player walks at 6 m/s from 30 m east; after it, he stands where he was.
            var dx = pt.Position.x - m.Position.x;
            var dz = pt.Position.z - m.Position.z;
            var range = math.sqrt(dx * dx + dz * dz);
            var t = now - mimicT0;
            if (mimicStartT < 0f && m.Mode != CreatureMode.Flee)
            {
                var from = math.max(1f, 30f - t * 6f);
                var p = new float2(m.Position.x + from, m.Position.z);
                pt.Position = new float3(p.x, ground.SampleHeight(p) + 0.1f, p.y);
                em.SetComponentData(player, pt);
            }
            if (mimicStartT < 0f && m.Mode == CreatureMode.Flee)
            {
                mimicStartT = now;
                mimicStartPos = m.Position;
                Debug.Log($"DebugControls: mimic STARTLE at {range:F1} m (t {t:F1} s): curl {m.Curl:F2}, lie {m.Lie:F2}");
            }
            if (mimicStartT >= 0f)
            {
                var since = now - mimicStartT;
                mimicMaxSpeed = math.max(mimicMaxSpeed, m.Speed);
                if (!mimicFolded && m.Curl < 0.1f)
                {
                    mimicFolded = true;
                    Debug.Log($"DebugControls: mimic FOLDED (curl < 0.1) {since:F2} s after the startle; lie {m.Lie:F2}");
                }
                if (!mimicRunMarked && since >= 1.5f)
                {
                    mimicRunMarked = true;
                    mimicRunPos = m.Position;
                    mimicRunT = now;
                }
                if (mimicShotT < 0f && since >= 2.2f && m.Mode == CreatureMode.Flee && shotIndex >= 0 && shotStarted >= 0f)
                {
                    mimicShotT = now;
                    shotWait = now - shotStarted + 0.05f;
                    Debug.Log($"DebugControls: mimic mid-run shot at {since:F2} s: speed {m.Speed:F2} m/s, {range:F1} m from the player, curl {m.Curl:F2}, lie {m.Lie:F2}");
                }
                if (mimicEndT < 0f && mimicRunMarked && m.Mode != CreatureMode.Flee)
                {
                    mimicEndT = now;
                    mimicEndPos = m.Position;
                    var span = math.max(now - mimicRunT, 0.01f);
                    Debug.Log($"DebugControls: mimic FLEE END {since:F2} s after the startle, mode {m.Mode}: max speed {mimicMaxSpeed:F2} m/s, mean speed {math.distance(mimicRunPos.xz, mimicEndPos.xz) / span:F2} m/s over {span:F1} s, {range:F1} m from the player");
                }
                if (mimicEndT >= 0f && !mimicSettled && m.Curl >= 0.95f)
                {
                    mimicSettled = true;
                    Debug.Log($"DebugControls: mimic SETTLED (curl >= 0.95) {now - mimicEndT:F2} s after the flee ended, mode {m.Mode}, lie {m.Lie:F2}");
                }
            }
            if (now - mimicLogAt > 0.5f)
            {
                mimicLogAt = now;
                Debug.Log($"DebugControls: mimic t {t:F1}: mode {m.Mode}, speed {m.Speed:F2}, curl {m.Curl:F2}, lie {m.Lie:F2}, {range:F1} m from the player");
            }
            // Side-on camera on the mimic, 9 m south of it.
            var focus = new Vector3(m.Position.x, m.Position.y + 1.4f, m.Position.z);
            var cam = focus + new Vector3(0f, 0.6f, -6f); // A-12: 6 m back: only the mimic in frame (the tree is 12+ m east)
            PoseCamera(cam, Quaternion.LookRotation(focus - cam));
        }
    }
}
