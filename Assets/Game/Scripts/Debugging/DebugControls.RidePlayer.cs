using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    public partial class DebugControls
    {
        /// <summary>"-rideplayer [rider|mount]" (A-104 row 3): the player rides; null when off, "" for a creature.</summary>
        string ridePlayer;
        bool rpDone;
        /// <summary>When the test began (Time.time; -1 before), and when the current step began.</summary>
        float rpAt = -1f, rpStepAt;
        int rpStep, rpEnded, rpBegun, rpNaN;
        /// <summary>The dog in the sim world, where it was put down, and as this machine draws it (the same entity solo).</summary>
        Entity rpDog, rpMount;
        float3 rpDogAt;
        float rpMaxOffset, rpMaxApart;
        /// <summary>A-126 row 4, the steered walk: how far the dog went, its fastest gait, how high it got off the floor.</summary>
        float rpTravel, rpTopSpeed, rpTopJump;
        float3 rpLastDog;
        bool rpPass = true, rpApplied;
        FaunaDirector rpHostDirector;
        float3 rpRemoteLast;
        float rpRemoteStillAt;
        readonly List<string> rpLines = new List<string>();
        static readonly (string name, RideOff expect)[] RpCases =
        {
            ("get down", RideOff.None), ("swim", RideOff.Swim), ("climb", RideOff.Climb), ("removed", RideOff.Death)
        };

        /// <summary>
        /// "-rideplayer": once the world has settled. Alone or hosting (with "-body Cat"): a dog put down beside the player,
        /// which the player gets on as the interact button would (InteractionInputSystem.TestRide; as host, on the dog's
        /// ghost in the client world). A-126 row 4: the player steers it round a circle for 12 s (PlayerInputSystem.TestMove,
        /// running from 6 s to 8 s, a jump at 10 s) while every frame the player's body is measured against its seat
        /// (Riding.Offset, as drawn here) and the player against the dog (the dog stands where the player is), and the
        /// frames with a NaN counted; then each way down in turn, getting back on first: the button,
        /// the dog swimming, climbing, and taken out of the world, each logged with why, where and how far over the floor.
        /// PASS when the player stayed on the seat (under 1 cm, and the dog under it), the dog went over 10 m, ran, and
        /// jumped, no NaN, and every case got down for the right reason, on the floor. "-rideplayer rider" and "-rideplayer mount" (one each, host and client, the
        /// mount the heavier body): see RidePlayerPair.
        /// </summary>
        void RidePlayerTest()
        {
            if (ridePlayer != null && rpDone && rpHostDirector != null && rpHostDirector.SimWorld != null && rpHostDirector.SimWorld.IsCreated &&
                EnsureLocations(out var hostSource))
            {
                // (The host's own test is done; a client may join later, and still gets its dog.)
                DogForClient(World.DefaultGameObjectInjectionWorld.EntityManager, rpHostDirector.SimWorld.EntityManager, rpHostDirector, hostSource);
                return;
            }
            if (ridePlayer == null || rpDone || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var input = world.GetExistingSystemManaged<InteractionInputSystem>();
            if (input == null)
                return;
            if (ridePlayer.Length > 0)
            {
                RidePlayerPair(em, input, source);
                return;
            }
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null)
                return;
            // (A joined client has no sim world: it steers a dog the host put down beside it.)
            if (director.SimWorld == null || !director.SimWorld.IsCreated)
            {
                if (world.IsClient())
                    RidePlayerClient(em, input, source, director);
                return;
            }
            if (!director.CanSpawn)
            {
                Debug.LogWarning("RidePlayer: needs a solo or hosting game (or rider/mount)");
                rpDone = true;
                return;
            }
            var sim = director.SimWorld.EntityManager;
            var playerEntity = playerQuery.GetSingletonEntity();
            if (rpAt < 0f)
            {
                var dog = director.SpeciesIndex("Dog");
                if (dog < 0)
                {
                    Debug.LogWarning("RidePlayer: no Dog species");
                    rpDone = true;
                    return;
                }
                var at = player.Position.xz + new float2(2.5f, 0f);
                rpDogAt = new float3(at.x, source.SampleHeight(at), at.y);
                rpDog = director.Spawn(dog, rpDogAt, 0f);
                var c = sim.GetComponentData<CreatureState>(rpDog);
                c.Mode = CreatureMode.Puppet;
                c.Speed = c.GaitSpeed = 0f;
                sim.SetComponentData(rpDog, c);
                rpAt = rpStepAt = Time.time;
                // (The ride counters are static: they carry over from the last play when the domain isn't reloaded.)
                rpBegun = PlayerRideSystem.Begun;
                rpEnded = PlayerRideSystem.Ended;
                rpStep = 0;
                Debug.Log($"RidePlayer: dog at ({rpDogAt.x:F1}, {rpDogAt.y:F2}, {rpDogAt.z:F1}), {(world == director.SimWorld ? "solo" : "hosting")}");
                return;
            }
            if (world != director.SimWorld)
            {
                rpHostDirector = director;
                DogForClient(em, sim, director, source);
            }
            // The dog as this machine draws it: itself alone, its ghost (the nearest creature to it) in a client world.
            if (!em.Exists(rpMount) && sim.HasComponent<CreatureState>(rpDog))
            {
                if (world == director.SimWorld)
                    rpMount = rpDog;
                else
                {
                    var dogAt = sim.GetComponentData<CreatureState>(rpDog).Position;
                    using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<GhostInstance>());
                    using var all = q.ToEntityArray(Allocator.Temp);
                    var best = 1.5f;
                    foreach (var e in all)
                    {
                        var d = math.distance(em.GetComponentData<CreatureState>(e).Position, dogAt);
                        if (d < best && !em.HasComponent<PlayerAvatar>(e) && !em.HasComponent<RemoteAvatar>(e))
                        {
                            best = d;
                            rpMount = e;
                        }
                    }
                }
            }
            var riding = em.HasComponent<PlayerRide>(playerEntity);
            var t = Time.time - rpStepAt;
            if (rpStep == 0)
            {
                if (!riding && Mathf.FloorToInt(t) != Mathf.FloorToInt(t - Time.deltaTime))
                    Debug.Log($"RidePlayer {t:F0} s: waiting, mount {(em.Exists(rpMount) ? "seen" : "not seen")}, begun {PlayerRideSystem.Begun}, refusal {PlayerRideSystem.LastRefusal}");
                // Get on, then the dog walks its circle for 12 s.
                if (!riding && t > 1.5f && t < 3f && em.Exists(rpMount) && PlayerRideSystem.Begun == rpBegun)
                {
                    rpBegun = PlayerRideSystem.Begun + 1;
                    input.TestRide(rpMount);
                    return;
                }
                if (riding && t < 14f)
                {
                    SteerDog(t);
                    Measure(em, playerEntity, source, t);
                    return;
                }
                if (t < 14f && !(t > 3f && !riding))
                    return;
                PlayerInputSystem.TestMove = null;
                PlayerInputSystem.TestSprint = false;
                rpLines.Add(riding || PlayerRideSystem.Begun > 0
                    ? $"walk: max offset {rpMaxOffset * 100f:F2} cm, player from the dog {rpMaxApart * 100f:F2} cm max, dog went {rpTravel:F1} m, " +
                      $"top gait {rpTopSpeed:F2} m/s, jumped {rpTopJump:F2} m, steered {SteeredMountSystem.Steered}, NaN frames {rpNaN}"
                    : "walk: player NOT on the dog");
                rpPass &= rpMaxOffset < 0.01f && rpMaxApart < 0.01f && rpNaN == 0 && PlayerRideSystem.Begun > 0 &&
                          rpTravel > 10f && rpTopJump > 0.2f && rpTopSpeed > 2f;
                NextStep();
                return;
            }
            var k = rpStep - 1;
            if (k >= RpCases.Length)
            {
                rpDone = true;
                Debug.Log("RidePlayer: " + string.Join(" | ", rpLines) + $" => {(rpPass ? "PASS" : "FAIL")}");
                return;
            }
            var c2 = RpCases[k];
            if (t < 0.5f)
                return;
            if (!rpApplied && !riding && t < 1.5f)
            {
                // Back on first (the dog stood still, as a puppet).
                if (em.Exists(rpMount) && PlayerRideSystem.Begun == rpBegun)
                {
                    rpBegun = PlayerRideSystem.Begun + 1;
                    rpEnded = PlayerRideSystem.Ended;
                    input.TestRide(rpMount);
                }
                return;
            }
            if (t > 2.5f && PlayerRideSystem.Ended == rpEnded && sim.HasComponent<CreatureState>(rpDog))
            {
                // The case, set again each frame till the player is down (the brain may change a mode it didn't pick).
                var m = sim.GetComponentData<CreatureState>(rpDog);
                switch (c2.name)
                {
                    case "get down": if (!rpApplied) input.TestRide(Entity.Null); break;
                    case "swim": m.Mode = CreatureMode.Swim; m.Timer = 30f; break;
                    case "climb": m.Mode = CreatureMode.Climb; m.Timer = 30f; break;
                    case "removed": sim.DestroyEntity(rpDog); break;
                }
                rpApplied = true;
                if (c2.name != "removed")
                    sim.SetComponentData(rpDog, m);
                return;
            }
            if (!rpApplied)
                return;
            if (PlayerRideSystem.Ended != rpEnded || t > 8f)
            {
                var down = PlayerRideSystem.Ended != rpEnded;
                var p = em.GetComponentData<LocalTransform>(playerEntity).Position;
                var floor = p.y - source.FloorUnder(p + new float3(0f, 0.5f, 0f), out _);
                var ok = down && PlayerRideSystem.LastOff == c2.expect && math.all(math.isfinite(p)) && math.abs(floor) < 0.05f &&
                         !em.HasComponent<PlayerRide>(playerEntity);
                rpPass &= ok;
                rpLines.Add(down ? $"{c2.name}: down ({PlayerRideSystem.LastOff}) at ({p.x:F2}, {p.y:F2}, {p.z:F2}), floor {floor:F2} m ({(ok ? "ok" : "WRONG")})"
                                 : $"{c2.name}: NOT down after 8 s (WRONG)");
                Debug.Log("RidePlayer: " + rpLines[rpLines.Count - 1]);
                if (sim.HasComponent<CreatureState>(rpDog))
                {
                    var m = sim.GetComponentData<CreatureState>(rpDog);
                    m.Mode = CreatureMode.Puppet;
                    m.Speed = m.GaitSpeed = 0f;
                    sim.SetComponentData(rpDog, m);
                }
                NextStep();
            }
        }

        void NextStep()
        {
            rpStep++;
            rpStepAt = Time.time;
            rpApplied = false;
            rpEnded = PlayerRideSystem.Ended;
        }

        /// <summary>A-126 row 4: steers the dog as a player would: round a circle on the stick, running 6-8 s, a jump at 10 s.</summary>
        static void SteerDog(float t)
        {
            var a = (t - 3f) * 0.6f;
            PlayerInputSystem.TestMove = new float2(math.cos(a), -math.sin(a)) * 0.8f;
            PlayerInputSystem.TestSprint = t >= 6f && t < 8f;
            // (Pressed through half a second, as a player presses when it's down: one frame might fall on a bump. Twice,
            // at 5 s and 10 s: on a steep climb a jump hardly clears the slope ahead, and the circle may cross one.)
            if ((t >= 5f && t < 5.5f) || (t >= 10f && t < 10.5f))
                PlayerInputSystem.TestJump = true;
        }

        /// <summary>One frame of the walk: the player's body against its seat, and the player against its body.</summary>
        void Measure(EntityManager em, Entity playerEntity, in TerrainSource source, float t)
        {
            var ride = em.GetComponentData<PlayerRide>(playerEntity);
            if (!playerAvatarFor(em, out var avatar) || !em.Exists(ride.Mount))
                return;
            var r = em.GetComponentData<CreatureState>(avatar);
            var m = em.GetComponentData<CreatureState>(ride.Mount);
            var rb = em.GetComponentData<CreatureBody>(avatar);
            var mb = em.GetComponentData<CreatureBody>(ride.Mount);
            var offset = Riding.Offset(ref rb.Blob.Value, r, rb.Coat, ref mb.Blob.Value, m, mb.Coat, source);
            var p = em.GetComponentData<LocalTransform>(playerEntity).Position;
            if (!math.all(math.isfinite(p)) || !math.isfinite(offset))
                rpNaN++;
            else if (t > 2f)
            {
                rpMaxOffset = math.max(rpMaxOffset, offset);
                // (Steering, the dog stands where the player is; carried, the player is where its body sits.)
                rpMaxApart = math.max(rpMaxApart, math.distance(p, ride.Steering ? m.Position : r.Position));
                if (rpLastDog.x != 0f || rpLastDog.z != 0f)
                    rpTravel += math.min(math.distance(m.Position.xz, rpLastDog.xz), 1f);
                rpLastDog = m.Position;
                rpTopSpeed = math.max(rpTopSpeed, m.Speed);
                rpTopJump = math.max(rpTopJump, m.Position.y - source.FloorUnder(m.Position + new float3(0f, 0.5f, 0f), out _));
            }
            if (Mathf.FloorToInt(t) != Mathf.FloorToInt(t - Time.deltaTime))
                Debug.Log($"RidePlayer {t:F0} s: dog ({m.Position.x:F1}, {m.Position.y:F2}, {m.Position.z:F1}) yaw {math.degrees(m.Yaw):F0}, " +
                          $"player ({p.x:F2}, {p.y:F2}, {p.z:F2}) {(RidePose)r.Stage}, offset {offset * 100f:F2} cm, " +
                          $"{(ride.Steering ? "steering" : "carried")}, gait {m.Speed:F2} m/s, mode {m.Mode}, holder {m.Holder}");
        }

        static bool playerAvatarFor(EntityManager em, out Entity avatar)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<CreatureState>());
            return q.TryGetSingletonEntity<PlayerAvatar>(out avatar);
        }

        /// <summary>A-126 row 4, host and client: the dog put down beside the joined player, and what the host saw of it.</summary>
        Entity rpClientDog;
        float3 rpClientDogLast;
        float rpClientDogTravel;
        int rpClientDogHolder;
        /// <summary>The client side: when it began waiting and when its current step began (Time.time; -1 before), the step.</summary>
        float rpNetAt = -1f, rpNetStepAt = -1f;
        int rpNetStep;

        /// <summary>
        /// Hosting "-rideplayer": once the host's own test is done and a joined player is in the world, a second dog put
        /// down beside it, for its "-rideplayer" to steer (RidePlayerClient). Every second the host logs that dog
        /// as the server has it: where, its mode and holder (the joined player's while it steers), how far it went.
        /// </summary>
        void DogForClient(EntityManager em, EntityManager sim, FaunaDirector director, in TerrainSource source)
        {
            // (Only once the host's own test is done, so the joined player can't take the host's dog, and once the joined
            // player has stood still 2 s: a joined player is placed after it first appears.)
            if (!rpDone)
                return;
            if (rpClientDog != Entity.Null && !sim.HasComponent<CreatureState>(rpClientDog))
            {
                if (rpClientDogHolder == 0)
                    rpClientDog = Entity.Null;
                return;
            }
            if (rpClientDog == Entity.Null)
            {
                using var others = em.CreateEntityQuery(ComponentType.ReadOnly<RemoteAvatar>(), ComponentType.ReadOnly<CreatureState>());
                if (others.IsEmpty)
                    return;
                using var them = others.ToEntityArray(Allocator.Temp);
                var there = em.GetComponentData<CreatureState>(them[0]).Position;
                if (math.distance(there, rpRemoteLast) > 0.5f)
                {
                    rpRemoteLast = there;
                    rpRemoteStillAt = Time.time;
                    return;
                }
                if (Time.time - rpRemoteStillAt < 2f)
                    return;
                var at = there.xz + new float2(-2.5f, 0f);
                rpClientDog = director.Spawn(director.SpeciesIndex("Dog"), new float3(at.x, source.SampleHeight(at), at.y), 0f);
                var c = sim.GetComponentData<CreatureState>(rpClientDog);
                c.Mode = CreatureMode.Puppet;
                c.Speed = c.GaitSpeed = 0f;
                sim.SetComponentData(rpClientDog, c);
                rpClientDogLast = c.Position;
                Debug.Log($"RidePlayer host: a dog for the joined player at ({at.x:F1}, {c.Position.y:F2}, {at.y:F1})");
                return;
            }
            var s = sim.GetComponentData<CreatureState>(rpClientDog);
            rpClientDogTravel += math.min(math.distance(s.Position.xz, rpClientDogLast.xz), 1f);
            rpClientDogLast = s.Position;
            rpClientDogHolder = math.max(rpClientDogHolder, s.Holder);
            if (Mathf.FloorToInt(Time.time) != Mathf.FloorToInt(Time.time - Time.deltaTime))
                Debug.Log($"RidePlayer host: the joined player's dog at ({s.Position.x:F1}, {s.Position.y:F2}, {s.Position.z:F1}), " +
                          $"mode {s.Mode}, holder {s.Holder} (most {rpClientDogHolder}), went {rpClientDogTravel:F1} m, gait {s.Speed:F2} m/s, steered {SteeredMountSystem.Steered}");
        }

        /// <summary>
        /// A joined client's "-rideplayer" (A-126 row 4): waits for the dog the host put down beside it (the nearest
        /// dog ghost no one holds), gets on, steers it as the solo test does (measured the same way: the dog as this
        /// machine draws it stands where the player is), then gets down with the button. PASS when it stayed on, went
        /// over 10 m, ran and jumped, no NaN, and got down on the floor.
        /// </summary>
        void RidePlayerClient(EntityManager em, InteractionInputSystem input, in TerrainSource source, FaunaDirector director)
        {
            var playerEntity = playerQuery.GetSingletonEntity();
            if (rpNetAt < 0f)
            {
                rpNetAt = Time.time;
                Debug.Log("RidePlayer client: waiting for a dog");
            }
            var riding = em.HasComponent<PlayerRide>(playerEntity);
            if (rpNetStep == 0)
            {
                if (Time.time - rpNetAt > 90f)
                {
                    Debug.Log("RidePlayer client: no dog came => FAIL");
                    rpDone = true;
                    return;
                }
                if (Time.time - rpNetAt < 3f)
                    return;
                // The nearest dog ghost no one holds, within 20 m.
                var dog = director.SpeciesIndex("Dog");
                var me = em.GetComponentData<LocalTransform>(playerEntity).Position;
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>(), ComponentType.ReadOnly<GhostInstance>());
                using var all = q.ToEntityArray(Allocator.Temp);
                var best = 20f;
                var pick = Entity.Null;
                foreach (var e in all)
                {
                    var c = em.GetComponentData<CreatureState>(e);
                    var d = math.distance(c.Position, me);
                    if (em.GetComponentData<CreatureBody>(e).Species == dog && c.Holder == 0 && d < best)
                    {
                        best = d;
                        pick = e;
                    }
                }
                if (pick == Entity.Null)
                    return;
                rpMount = pick;
                rpBegun = PlayerRideSystem.Begun;
                input.TestRide(pick);
                rpNetStep = 1;
                rpNetStepAt = Time.time;
                Debug.Log($"RidePlayer client: getting on the dog {best:F1} m off");
                return;
            }
            var t = Time.time - rpNetStepAt;
            if (rpNetStep == 1)
            {
                if (!riding)
                {
                    if (t > 3f)
                    {
                        Debug.Log($"RidePlayer client: NOT on the dog (refusal {PlayerRideSystem.LastRefusal}) => FAIL");
                        rpDone = true;
                    }
                    return;
                }
                // (On the solo walk's clock, from 3 s to 14 s.)
                var walk = t + 2f;
                if (walk < 14f)
                {
                    SteerDog(walk);
                    Measure(em, playerEntity, source, walk);
                    return;
                }
                PlayerInputSystem.TestMove = null;
                PlayerInputSystem.TestSprint = false;
                rpLines.Add($"walk: max offset {rpMaxOffset * 100f:F2} cm, player from the dog {rpMaxApart * 100f:F2} cm max, dog went {rpTravel:F1} m, " +
                            $"top gait {rpTopSpeed:F2} m/s, jumped {rpTopJump:F2} m, NaN frames {rpNaN}");
                rpPass &= rpMaxOffset < 0.01f && rpMaxApart < 0.01f && rpNaN == 0 && rpTravel > 10f && rpTopJump > 0.2f && rpTopSpeed > 2f;
                rpEnded = PlayerRideSystem.Ended;
                input.TestRide(Entity.Null);
                rpNetStep = 2;
                rpNetStepAt = Time.time;
                return;
            }
            if (PlayerRideSystem.Ended != rpEnded || t > 8f)
            {
                var down = PlayerRideSystem.Ended != rpEnded;
                var p = em.GetComponentData<LocalTransform>(playerEntity).Position;
                var floor = p.y - source.FloorUnder(p + new float3(0f, 0.5f, 0f), out _);
                var ok = down && PlayerRideSystem.LastOff == RideOff.None && math.abs(floor) < 0.05f && !riding;
                rpPass &= ok;
                rpLines.Add(down ? $"get down: down ({PlayerRideSystem.LastOff}) at ({p.x:F2}, {p.y:F2}, {p.z:F2}), floor {floor:F2} m ({(ok ? "ok" : "WRONG")})"
                                 : "get down: NOT down after 8 s (WRONG)");
                rpDone = true;
                Debug.Log("RidePlayer client: " + string.Join(" | ", rpLines) + $" => {(rpPass ? "PASS" : "FAIL")}");
            }
        }

        /// <summary>
        /// "-rideplayer rider" / "-rideplayer mount": two players, one each side (host and client, either way round;
        /// the mount the heavier body, "-body Human" for it and "-body Cat" for the rider). The clock starts when this
        /// side first sees the other player's body. Rider: at 2 s stands beside the other body, at 3 s gets on it; at 28 s
        /// gets back on if it was put down, at 34 s gets down with the button. Mount: walks a circle from 4 s to 16 s (the
        /// rider carried), at 10 s tries to ride its rider (refused: a pair can't ride each other), lies down at 20 s
        /// (the rider must get down: Sleep) and gets up at 24 s. Each side logs every second where the rider sits in the
        /// mount's frame (right, up, forward) as it draws them: the two sides' lines should agree. Every way down logged.
        /// </summary>
        void RidePlayerPair(EntityManager em, InteractionInputSystem input, in TerrainSource source)
        {
            var playerEntity = playerQuery.GetSingletonEntity();
            using var others = em.CreateEntityQuery(ComponentType.ReadOnly<RemoteAvatar>(), ComponentType.ReadOnly<CreatureState>());
            if (others.IsEmpty)
                return;
            var other = others.ToEntityArray(Allocator.Temp)[0];
            if (!playerAvatarFor(em, out var avatar))
                return;
            if (rpAt < 0f)
            {
                rpAt = Time.time;
                rpBegun = PlayerRideSystem.Begun;
                rpEnded = PlayerRideSystem.Ended;
                Debug.Log($"RidePlayer {ridePlayer}: the other player is here");
                return;
            }
            var t = Time.time - rpAt;
            var last = t - Time.deltaTime;
            bool At(float s) => t >= s && last < s;
            var me = em.GetComponentData<CreatureState>(avatar);
            var them = em.GetComponentData<CreatureState>(other);
            var rider = ridePlayer == "rider";
            var riding = em.HasComponent<PlayerRide>(playerEntity);
            if (rider)
            {
                if (At(2f))
                {
                    // Beside the other body, on the ground.
                    var tr = em.GetComponentData<LocalTransform>(playerEntity);
                    var to = them.Position.xz + new float2(1.2f, 0f);
                    tr.Position = new float3(to.x, source.SampleHeight(to), to.y);
                    em.SetComponentData(playerEntity, tr);
                }
                if (At(3f) || At(28f) && !riding)
                    input.TestRide(other);
                if (At(34f) && riding)
                    input.TestRide(Entity.Null);
            }
            else
            {
                if (t >= 4f && t < 16f)
                {
                    var a = (t - 4f) * 0.5f;
                    PlayerInputSystem.TestMove = new float2(math.sin(a), math.cos(a)) * 0.5f;
                }
                else if (At(16f))
                    PlayerInputSystem.TestMove = null;
                if (At(10f))
                    input.TestRide(other);
                if (At(10.5f))
                    Debug.Log($"RidePlayer mount: riding my rider: {(em.HasComponent<PlayerRide>(playerEntity) ? "MOUNTED (WRONG)" : $"refused ({PlayerRideSystem.LastRefusal})")}");
                if (At(20f))
                    input.Rest = IdleAction.LieBack;
                if (At(24f))
                    input.Rest = IdleAction.None;
            }
            if (PlayerRideSystem.Begun != rpBegun || PlayerRideSystem.Ended != rpEnded)
            {
                Debug.Log($"RidePlayer {ridePlayer} {t:F1} s: {(PlayerRideSystem.Begun != rpBegun ? "got on" : $"got down ({PlayerRideSystem.LastOff})")}" +
                          $" at ({me.Position.x:F2}, {me.Position.y:F2}, {me.Position.z:F2})");
                rpBegun = PlayerRideSystem.Begun;
                rpEnded = PlayerRideSystem.Ended;
            }
            // Where the rider sits in the mount's frame, as drawn here (ours on theirs, or theirs on ours).
            var r = rider ? me : them;
            var m = rider ? them : me;
            var rel = math.mul(quaternion.RotateY(-m.Yaw), r.Position - m.Position);
            if (!math.all(math.isfinite(r.Position)))
                rpNaN++;
            var net = em.GetComponentData<RemoteAvatar>(other).Ghost;
            var look = em.HasComponent<NetPlayerLook>(net) ? em.GetComponentData<NetPlayerLook>(net).Mount : -1;
            if (Mathf.FloorToInt(t) != Mathf.FloorToInt(last))
                Debug.Log($"RidePlayer {ridePlayer} {t:F0} s: {(riding ? "riding" : "on foot")}, their Mount {look}, mount ({m.Position.x:F2}, {m.Position.y:F2}, " +
                          $"{m.Position.z:F2}) yaw {math.degrees(m.Yaw):F0}, rider in its frame ({rel.x:F3}, {rel.y:F3}, {rel.z:F3}) {(RidePose)r.Stage}, NaN {rpNaN}");
            if (t > 40f)
            {
                rpDone = true;
                Debug.Log($"RidePlayer {ridePlayer}: done, NaN frames {rpNaN}");
            }
        }
    }
}
