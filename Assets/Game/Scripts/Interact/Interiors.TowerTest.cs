using Punchy.Flora;
using Punchy.Player;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Interact
{
    /// <summary>
    /// P-51 rows 2-3 (#337): "-climbtower N", the test that an elf tower walked into (StructureKit.WalkInTower, with
    /// -seamless) can be climbed, up through the roof hatch and out onto the roof deck. With -atdoor N and -usedoor N, once door N stands open the player walks in to the
    /// ground floor, then up the stair tread by tread (each time to the lowest tread above its feet, by the kit boxes the
    /// stair is made of), through the hatch and out to the middle of the roof deck. Logs each tread reached, the storeys
    /// and the height got to, then PASS (on the deck, which counts as outside the tower) or FAIL (a fall off the stair, or
    /// 70 s up). One shot from the top with -shotdir. P-52 (#518): with -stallframe, one frame on the stair 2 m up stalls
    /// 0.5 s with the player walking along the wall, which must not carry it off the tread or through the wall;
    /// -nosubstep moves that long frame in one piece again (the old way, which fails).
    /// </summary>
    public partial class Interiors
    {
        int climbTower = -2, climbTreads;
        float climbAt, climbLog, climbBest, climbTop = float.NaN, climbStep = float.NaN;
        bool climbIn, climbDone, climbEntered, climbCentred, climbOnStair, climbStuckLogged;
        float3 climbLastPos, climbBestAt;
        bool climbStall, climbStalled;
        float climbLongestFrame;
        float climbStillSince;
        float2 climbStepAt = float.NaN;

        void ClimbTowerTest()
        {
            if (climbTower == -2)
            {
                var args = System.Environment.GetCommandLineArgs();
                var at = System.Array.IndexOf(args, "-climbtower");
                climbTower = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var n) ? n : -1;
                climbStall = System.Array.IndexOf(args, "-stallframe") >= 0;
                PlayerMovementSystem.NoSubstep.Data = System.Array.IndexOf(args, "-nosubstep") >= 0;
            }
            if (climbTower < 0 || climbDone || !Doors.TryGetDoor(climbTower, out var door, out var doorFacing))
                return;
            if (climbAt <= 0f)
            {
                if (!FloraDoors.IsOpen(door, 1f))
                    return;
                climbAt = Time.realtimeSinceStartup;
                Debug.Log($"Interiors: -climbtower {climbTower}: door open{(walkInOf.ContainsKey(climbTower) ? "" : " (not a walk-in door)")}");
            }
            if (!walkInOf.TryGetValue(climbTower, out var house) || !TryPlayer(out _, out var player))
            {
                Finish(false, "no walk-in tower at that door");
                return;
            }
            var w = walkIns[house];
            var t = Time.realtimeSinceStartup - climbAt;
            var feet = player.Position.y;
            climbLongestFrame = math.max(climbLongestFrame, Time.deltaTime);
            if (feet - w.Floor > climbBest)
                (climbBest, climbBestAt) = (feet - w.Floor, player.Position);
            // (A fall off the stair: log what stood round the highest point reached, and fail.)
            if (climbBest - (feet - w.Floor) > 1f)
            {
                using var fq = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
                if (fq.TryGetSingletonBuffer<KitBox>(out var near, true))
                    foreach (var box in near)
                    {
                        var local = math.mul(math.inverse(box.Rotation), climbBestAt + new float3(0f, 0.9f, 0f) - box.Centre);
                        if (math.all(math.abs(local) - box.Half <= 1.1f))
                            Debug.Log($"Interiors: -climbtower near the fall: box at {box.Centre}, half {box.Half}, local {local}");
                    }
                Finish(false, $"fell from {climbBestAt} to {player.Position}, longest frame {climbLongestFrame:F3} s");
                return;
            }
            // (P-52: the one stalled frame, walking along the wall on a tread.)
            if (climbStall && !climbStalled && climbOnStair && feet - w.Floor > 2f)
            {
                climbStalled = true;
                Debug.Log($"Interiors: -climbtower stalling one frame 0.5 s at {player.Position}{(PlayerMovementSystem.NoSubstep.Data ? " (no substeps)" : "")}");
                System.Threading.Thread.Sleep(500);
            }

            // In first: to a point on the ground floor 0.4 of the way in from the wall by the doorway.
            var to = w.Centre;
            if (!climbIn)
            {
                // (As -walkin: to the door's step, the low box in front of it, then straight in along the doorway's line;
                // aimed at a point off that line, the player catches on the jamb.)
                var f = math.normalizesafe(doorFacing);
                if (math.any(math.isnan(climbStepAt)))
                {
                    climbStepAt = door.xz;
                    using var sq = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
                    if (sq.TryGetSingletonBuffer<KitBox>(out var steps, true))
                        foreach (var kbox in steps)
                            if (kbox.Half.y < 0.16f && kbox.Half.x < 1.2f && math.abs(kbox.Centre.y - kbox.Half.y - door.y) < 0.2f && math.distance(kbox.Centre.xz, door.xz) < 3f)
                                climbStepAt = kbox.Centre.xz;
                }
                var entry = climbStepAt + f * 0.8f;
                var inside = climbStepAt - f * 1.6f;
                to = climbEntered ? inside : entry;
                climbEntered |= math.distance(entry, player.Position.xz) < 0.35f;
                climbIn = math.distance(inside, player.Position.xz) < 0.4f;
            }
            else
            {
                // The lowest stair tread with its top above the feet (treads: about 0.95 x 0.4 m boxes, Half.y/Half.x ~0.42).
                var found = false;
                var best = float.MaxValue;
                var top = 0f;
                using var q = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
                if (q.TryGetSingletonBuffer<KitBox>(out var boxes, true))
                    foreach (var box in boxes)
                    {
                        var ratio = box.Half.y / math.max(box.Half.x, 1e-3f);
                        if (box.Half.x < 0.4f || box.Half.x > 0.65f || math.abs(ratio - 0.42f) > 0.03f || math.distance(box.Centre.xz, w.Centre) > w.Radius + 0.5f)
                            continue;
                        var boxTop = box.Centre.y + box.Half.y;
                        top = math.max(top, boxTop);
                        if (boxTop > feet + 0.05f && boxTop < best)
                            (best, found, to) = (boxTop, true, box.Centre.xz);
                    }
                if (float.IsNaN(climbTop))
                {
                    climbTop = top;
                    Debug.Log($"Interiors: -climbtower {climbTower}: stair's top tread {top - w.Floor:F2} m up, eaves {w.Height:F2} m");
                }
                // (Each new tread aimed for is one more climbed.)
                if (found && best != climbStep)
                {
                    if (!float.IsNaN(climbStep))
                        climbTreads++;
                    climbStep = best;
                }
                // (From the door, first to the middle of the floor: a straight line from the door's side to the stair's
                // foot runs along the round wall and lodges on its segments.)
                climbCentred |= math.distance(w.Centre, player.Position.xz) < 0.5f;
                // (Each tread aimed at a little toward the wall: off its inner edge the player drops down the well.)
                if (found)
                    to += math.normalizesafe(to - w.Centre) * 0.15f;
                if (!climbCentred)
                    to = w.Centre;
                else if (found && !climbOnStair)
                {
                    // (Onto the first tread along the wall, from before the stair's foot, as one walks up: straight out from
                    // the middle, the player rubs on the second tread's side, too high to step.)
                    var foot = to - w.Centre;
                    var back = math.radians(-25f);
                    var before = w.Centre + new float2(foot.x * math.cos(back) - foot.y * math.sin(back), foot.x * math.sin(back) + foot.y * math.cos(back));
                    climbOnStair = math.distance(before, player.Position.xz) < 0.3f || feet > w.Floor + 0.2f;
                    if (!climbOnStair)
                        to = before;
                }
                else if (!found)
                {
                    // Past the top tread: out of the hatch onto the roof deck's middle.
                    to = w.Centre;
                    var roof = walkIns[house].Roof;
                    if (math.distance(w.Centre, player.Position.xz) < 0.6f && feet > climbTop - 0.05f && !float.IsNaN(roof) && feet >= roof - 0.3f)
                    {
                        var inside = InWalkIn(player.Position, out var inHouse, out _) && inHouse == house;
                        Finish(!inside, $"on the roof deck, {feet - w.Floor:F2} m up (deck {roof - w.Floor:F2}), {climbTreads} treads climbed" +
                                        (climbStalled ? $" through a stalled frame, longest frame {climbLongestFrame:F3} s" : "") +
                                        (inside ? ", but still counted inside" : ""));
                        return;
                    }
                }
            }
            PlayerInputSystem.TestMove = math.distance(to, player.Position.xz) > 0.2f ? math.normalizesafe(to - player.Position.xz) : float2.zero;

            if (t >= climbLog)
            {
                climbLog = t + 0.5f;
                InWalkIn(player.Position, out var inside, out var depth);
                Debug.Log($"Interiors: -climbtower {(climbIn ? "climb" : "in")}: at ({player.Position.x:F2}, {feet:F2}, {player.Position.z:F2}), {feet - w.Floor:F2} m up " +
                          $"(best {climbBest:F2}), house {inside}, depth {depth:F2}, toward ({to.x:F2}, {to.y:F2})");
            }
            // (Stuck: no move for 4 s. Log once what stands within 0.9 m of the player, to see what blocks it.)
            if (math.distance(climbLastPos, player.Position) > 0.05f) { climbLastPos = player.Position; climbStillSince = t; }
            else if (!climbStuckLogged && t - climbStillSince > 4f)
            {
                climbStuckLogged = true;
                Debug.Log($"Interiors: -climbtower stuck at {player.Position}, toward {to}, door {door} facing {doorFacing}, step {climbStepAt}, open {FloraDoors.IsOpen(door, 1f)}");
                using var bq = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
                if (bq.TryGetSingletonBuffer<KitBox>(out var near, true))
                    foreach (var box in near)
                    {
                        var local = math.mul(math.inverse(box.Rotation), player.Position + new float3(0f, 0.9f, 0f) - box.Centre);
                        var gap = math.max(math.abs(local) - box.Half - new float3(0.9f, 0.9f, 0.9f), 0f);
                        if (math.all(gap <= 0f))
                            Debug.Log($"Interiors: -climbtower near box at {box.Centre}, half {box.Half}, player local {local}");
                    }
            }
            if (t > 70f)
                Finish(false, $"timed out at {feet - w.Floor:F2} m up (best {climbBest:F2}, top tread {climbTop - w.Floor:F2})");

            void Finish(bool pass, string why)
            {
                PlayerInputSystem.TestMove = null;
                climbDone = true;
                Debug.Log($"Interiors: -climbtower {climbTower} {(pass ? "PASS" : "FAIL")}: {why}");
                var dir = ShotDir();
                if (pass && dir != null && Punchy.Debugging.ShotTool.Take(System.IO.Path.Combine(dir, "tower-top.png"), 640, 360, 20f, true))
                    Debug.Log("Interiors: -climbtower shot tower-top");
            }
        }
    }
}
