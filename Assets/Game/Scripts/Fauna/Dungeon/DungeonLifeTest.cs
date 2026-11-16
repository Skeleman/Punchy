using Punchy.Dungeon;
using Punchy.Net;
using Punchy.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Test only (#24 phase 2 row 2): "-dungeonlife" with "-guardtest" checks that dungeon creatures are residents. Once
    /// the guard test has put the player by a guarded chest and residents are in the world, solo or the host kills one
    /// (sets it Gone), checks its dead bit is set, takes the player 700 m away until the Dungeon tier is empty, brings it
    /// back and checks the residents are made again without the dead one. A host with another player in the world skips
    /// the trip (the other player keeps them in). A joined client logs the creatures it is sent near it, and (row 3)
    /// waits 20 s for the host's dead masks. Logs "[dungeon life] PASS" or "FAIL ...", DungeonState.Describe() and the
    /// director's DungeonDigest(), which host and joiner compare.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class DungeonLifeTestSystem : SystemBase
    {
        EntityQuery player, replicas;
        FaunaDirector director;
        int stage;
        float stageAt, startAt = -1f;
        float3 home;
        uint key;
        int ordinal, leftBefore;
        Entity victim;

        protected override void OnCreate()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            Enabled = System.Array.IndexOf(args, "-dungeonlife") >= 0 && System.Array.IndexOf(args, "-guardtest") >= 0;
            player = new EntityQueryBuilder(Allocator.Temp).WithAll<PlayerTag, LocalTransform, PlayerMotion>().Build(this);
            replicas = new EntityQueryBuilder(Allocator.Temp).WithAll<CreatureReplica, CreatureState>().Build(this);
            RequireForUpdate(player);
        }

        protected override void OnUpdate()
        {
            var now = (float)SystemAPI.Time.ElapsedTime;
            if (startAt < 0f)
                startAt = now;
            // (After the guard test's hold, so the player stands by the chest.)
            if (now < startAt + 8f)
                return;
            if (director == null)
                director = Object.FindFirstObjectByType<FaunaDirector>();
            var host = false;
            foreach (var w in World.All)
                host |= w.IsServer();
            var p = player.GetSingletonEntity();
            var at = EntityManager.GetComponentData<LocalTransform>(p).Position;
            if (World.IsClient() && !host)
            {
                Joined(at, now);
                return;
            }
            if (director == null || director.SimWorld == null)
                return;
            var sim = director.SimWorld.EntityManager;
            switch (stage)
            {
                case 0: // residents in the world: pick one and kill it
                    victim = director.AnyLiveDungeonResident(out key, out ordinal);
                    if (victim == Entity.Null)
                    {
                        if (now > startAt + 60f)
                            Finish("FAIL no dungeon resident was made near the player in 60 s");
                        return;
                    }
                    director.DungeonResidentsLeft(key, DungeonState.DrivenOff, out var residents, out leftBefore);
                    // #24 phase 2 row 4: the props by the chest, as drawn ("-shotdir" for a picture of them).
                    Debug.Log($"[dungeon life] props near the chest: {DungeonProps.Describe()}");
                    var shot = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-shotdir");
                    if (shot >= 0 && shot + 1 < Punchy.Debugging.DebugControls.LaunchArgs.Length)
                    {
                        var dir = Punchy.Debugging.DebugControls.LaunchArgs[shot + 1];
                        System.IO.Directory.CreateDirectory(dir);
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "dungeon-props-near.png"));
                    }
                    var s = sim.GetComponentData<CreatureState>(victim);
                    Debug.Log($"[dungeon life] {World.Name}: tier {director.DungeonCount}/{FaunaDirector.MaxDungeon}; dungeon {key:X8} " +
                              $"{leftBefore}/{residents} at their posts; killing resident {ordinal} at ({s.Position.x:F1}, {s.Position.y:F1}, {s.Position.z:F1})");
                    s.Gone = true;
                    sim.SetComponentData(victim, s);
                    Next(now);
                    break;
                case 1: // its bit set, the count one lower
                    if (now < stageAt + 1.5f)
                        return;
                    director.DungeonResidentsLeft(key, DungeonState.DrivenOff, out _, out var left);
                    var dead = (director.DungeonDeadMask(key) & 1ul << ordinal) != 0;
                    Debug.Log($"[dungeon life] after the kill: dead mask {director.DungeonDeadMask(key):X16} (bit {ordinal} {(dead ? "set" : "NOT set")}), " +
                              $"{left} at their posts (was {leftBefore}), victim {(sim.Exists(victim) ? "still in the world" : "removed")}");
                    if (!dead || left != leftBefore - 1)
                    {
                        Finish("FAIL the kill was not recorded");
                        return;
                    }
                    if (director.DungeonPlayers > 1)
                    {
                        Finish($"PASS (kill recorded; trip skipped: {director.DungeonPlayers} players in the world)");
                        return;
                    }
                    home = at;
                    Next(now);
                    break;
                case 2: // away until the Dungeon tier is empty
                    Hold(p, home + new float3(700f, 0f, 0f));
                    if (director.DungeonCount == 0 && now > stageAt + 1f)
                    {
                        Debug.Log($"[dungeon life] away: dungeon tier empty after {now - stageAt:F1} s; props: {DungeonProps.Describe()}");
                        Next(now);
                    }
                    else if (now > stageAt + 20f)
                        Finish($"FAIL dungeon tier still {director.DungeonCount} 20 s after leaving");
                    break;
                case 3: // back: made again, all but the dead one
                    Hold(p, home);
                    if (now < stageAt + 6f)
                        return;
                    director.DungeonResidentsLeft(key, DungeonState.DrivenOff, out var all, out var back);
                    var again = director.DungeonResidentLive(key, ordinal);
                    Debug.Log($"[dungeon life] back: tier {director.DungeonCount}/{FaunaDirector.MaxDungeon}; dungeon {key:X8} {back}/{all} at their posts, " +
                              $"dead resident {ordinal} {(again ? "MADE AGAIN" : "not made")}");
                    Finish(director.DungeonCount > 0 && !again && back == leftBefore - 1
                        ? "PASS"
                        : "FAIL residents not made again as recorded");
                    break;
            }
        }

        void Joined(float3 at, float now)
        {
            using var states = replicas.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var near = 0;
            foreach (var s in states)
                if (math.distance(s.Position, at) < 40f)
                    near++;
            // (Row 3: past 20 s, so a late joiner has had the host's masks.)
            if (near > 0 && now > startAt + 20f || now > startAt + 60f)
                Finish(near > 0 ? $"PASS joined client: {near} creatures within 40 m (of {states.Length} sent)"
                                : "FAIL joined client: no creature within 40 m in 60 s");
        }

        void Hold(Entity p, float3 where)
        {
            var t = EntityManager.GetComponentData<LocalTransform>(p);
            t.Position = where;
            EntityManager.SetComponentData(p, t);
            var m = EntityManager.GetComponentData<PlayerMotion>(p);
            m.VerticalVelocity = 0f;
            EntityManager.SetComponentData(p, m);
        }

        void Next(float now)
        {
            stage++;
            stageAt = now;
        }

        void Finish(string result)
        {
            Debug.Log($"[dungeon life] {World.Name}: {result} | {DungeonState.Describe()} | records: {director?.DungeonDigest()}");
            Enabled = false;
        }
    }
}
