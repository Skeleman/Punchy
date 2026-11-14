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
        bool rpPass = true, rpApplied;
        readonly List<string> rpLines = new List<string>();
        static readonly (string name, RideOff expect)[] RpCases =
        {
            ("get down", RideOff.None), ("swim", RideOff.Swim), ("climb", RideOff.Climb), ("removed", RideOff.Death)
        };

        /// <summary>
        /// "-rideplayer": once the world has settled. Alone or hosting (with "-body Cat"): a dog put down beside the player,
        /// which the player gets on as the interact button would (InteractionInputSystem.TestRide; as host, on the dog's
        /// ghost in the client world); the dog walks a circle for 12 s while every frame the player's body is measured
        /// against its seat (Riding.Offset, as drawn here) and the player against its body (the camera follows the
        /// player), and the frames with a NaN counted; then each way down in turn, getting back on first: the button,
        /// the dog swimming, climbing, and taken out of the world, each logged with why, where and how far over the floor.
        /// PASS when the player stayed on the seat (under 1 cm, and with its body), no NaN, and every case got down for
        /// the right reason, on the floor. "-rideplayer rider" and "-rideplayer mount" (one each, host and client, the
        /// mount the heavier body): see RidePlayerPair.
        /// </summary>
        void RidePlayerTest()
        {
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
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return;
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
                rpStep = 0;
                Debug.Log($"RidePlayer: dog at ({rpDogAt.x:F1}, {rpDogAt.y:F2}, {rpDogAt.z:F1}), {(world == director.SimWorld ? "solo" : "hosting")}");
                return;
            }
            // The dog as this machine draws it: itself alone, its ghost (the nearest creature to it) in a client world.
            if (!em.Exists(rpMount) && sim.Exists(rpDog))
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
                    DriveDog(sim, true);
                    Measure(em, playerEntity, source, t);
                    return;
                }
                if (t < 14f && !(t > 3f && !riding))
                    return;
                DriveDog(sim, false);
                rpLines.Add(riding || PlayerRideSystem.Begun > 0
                    ? $"walk: max offset {rpMaxOffset * 100f:F2} cm, player from its body {rpMaxApart * 100f:F2} cm max, NaN frames {rpNaN}"
                    : "walk: player NOT on the dog");
                rpPass &= rpMaxOffset < 0.01f && rpMaxApart < 0.01f && rpNaN == 0 && PlayerRideSystem.Begun > 0;
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
            if (t > 2.5f && PlayerRideSystem.Ended == rpEnded && sim.Exists(rpDog))
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
                if (sim.Exists(rpDog))
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

        /// <summary>Walks the test's dog round its circle (as RideTest's mounts), or stands it still facing +x.</summary>
        void DriveDog(EntityManager sim, bool walk)
        {
            if (!sim.Exists(rpDog))
                return;
            var m = sim.GetComponentData<CreatureState>(rpDog);
            var c = rpDogAt - new float3(RideCircle, 0f, 0f);
            if (walk)
            {
                var angle = math.atan2(m.Position.x - c.x, m.Position.z - c.z);
                m.Yaw = angle - math.PI * 0.5f - math.clamp((math.distance(m.Position.xz, c.xz) - RideCircle) * 0.3f, -0.3f, 0.3f);
                m.Speed = m.GaitSpeed = RideWalk;
            }
            else
                m.Speed = m.GaitSpeed = 0f;
            sim.SetComponentData(rpDog, m);
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
                rpMaxApart = math.max(rpMaxApart, math.distance(p, r.Position));
            }
            if (Mathf.FloorToInt(t) != Mathf.FloorToInt(t - Time.deltaTime))
                Debug.Log($"RidePlayer {t:F0} s: dog ({m.Position.x:F1}, {m.Position.y:F2}, {m.Position.z:F1}) yaw {math.degrees(m.Yaw):F0}, " +
                          $"player ({p.x:F2}, {p.y:F2}, {p.z:F2}) {(RidePose)r.Stage}, offset {offset * 100f:F2} cm");
        }

        static bool playerAvatarFor(EntityManager em, out Entity avatar)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<CreatureState>());
            return q.TryGetSingletonEntity<PlayerAvatar>(out avatar);
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
