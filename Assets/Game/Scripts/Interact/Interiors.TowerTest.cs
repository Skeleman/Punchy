using Punchy.Flora;
using Punchy.Player;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Interact
{
    /// <summary>
    /// P-51 row 2 (#337): "-climbtower N", the test that an elf tower walked into (StructureKit.WalkInTower, with
    /// -seamless) can be climbed. With -atdoor N and -usedoor N, once door N stands open the player walks in to the
    /// ground floor, then up the stair tread by tread (each time to the lowest tread above its feet, by the kit boxes the
    /// stair is made of), and out onto the top storey's floor. Logs each tread reached, the storeys and the height got
    /// to, then PASS (on the top floor, inside the tower) or FAIL (70 s up). One shot from the top with -shotdir.
    /// </summary>
    public partial class Interiors
    {
        int climbTower = -2, climbTreads;
        float climbAt, climbLog, climbBest, climbTop = float.NaN, climbStep = float.NaN;
        bool climbIn, climbDone;

        void ClimbTowerTest()
        {
            if (climbTower == -2)
            {
                var args = System.Environment.GetCommandLineArgs();
                var at = System.Array.IndexOf(args, "-climbtower");
                climbTower = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var n) ? n : -1;
            }
            if (climbTower < 0 || climbDone || !Doors.TryGetDoor(climbTower, out var door, out _))
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
            climbBest = math.max(climbBest, feet - w.Floor);

            // In first: to a point on the ground floor 0.4 of the way in from the wall by the doorway.
            var to = w.Centre;
            if (!climbIn)
            {
                var inside = w.Centre + w.Facing * w.Radius * 0.4f;
                to = inside;
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
                if (!found)
                {
                    // Past the top tread: out onto the floor's middle.
                    to = w.Centre;
                    if (math.distance(w.Centre, player.Position.xz) < 0.6f && feet > climbTop - 0.05f)
                    {
                        Finish(InWalkIn(player.Position, out var inHouse, out _) && inHouse == house,
                               $"on the top floor, {feet - w.Floor:F2} m up, {climbTreads} treads climbed");
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
