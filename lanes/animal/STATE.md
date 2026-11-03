# Animal lane: state (2026-11-02, A-35 paused for Zach's demo)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin 4c03cde (A-35 gesture) on bf170f2 (merge of
main 41a9259, clean, `--no-ff`). PlayerMovementSystem keeps KeepOutOfTower and PushOutOfSolids. Lane hashes are now
the merged ones: v2 126 / 053C63D5, v1 102 / 51EA6463 (not yet re-read from a player log since the merge).
Untracked `Assets/_Recovery/` is old and not ours. Editor on port 7800.
Builds/Windows/Punchy.exe was built from 4c03cde (A-35, Succeeded) but NOT run: paused before the shot run.

## A-35 left to do (relaunch)
- Run `Temp/a35_shots.txt` (in the Punchy tree) once with `-quit`, shots to PunchyMilestones/animal/a35/raw, resize
  to 800 px, view the pairs, read the -walk log lines (pushed m, height over floor) and the two hashes.
- `-watchfrom 1.6,2.5` for the merchant front-on is a guess at the sign: if it shows the back, flip to -2.5.

## Slope pair candidate (A-35 probe, editor play mode)
- Probe: Temp/a35_probe.cs. Every cave segment over 25 deg, 2.5 m walks uphill (and +-0.6 off axis), floor sampled
  every 0.25 m; kept walks of 40-65 deg with no drop over 0.5 m; simulated with and without PushOutOfSolids.
- Picked seg 245: -goto -1448.0,83.2,238.7 (floor 82.38) -walk -1448.4,86.2,241.1. Floor rise every 0.25 m:
  .27 .18 .15 .21 .58 .59 .31 .36 .54 .38 (3.57 m in 2.5 m, 55 deg; never drops). Behind the start it falls
  2.4 m in 3 m.
- Sim: no push-out walks up (0.3 m to go) with the body 0.26 m into the slope; push-out slides it back down
  (SteepFloor, 4.4 m from the end, pushed 12.1 m, never airborne). So the pair should differ by ~4 m of position.
- Camera: passage is tight (only ~10 clear spots). -camrel 2.10,3.41,-0.44,-0.20,2.91,1.20 (side, 2 m back down
  the slope, 0.7 m clear of rock on all sight lines).
- Gotcha: right after entering play mode the Default World briefly has a 278-segment cave set (not v2's 541);
  wait for it before probing or coordinates won't match the player.

## Merchant gesture (A-35, built, not seen)
- CreatureAnimator Gesture: one hand at a time raised ~1.9 rad for ~40% of a 3.8 s cycle (was 1.2 rad, ~0.4 s),
  forearm beckoning at 8 rad/s. OgreCamp's merchant is a goblin: `-watch *:Merchant`.

## Job work poses and props (A-33/34)
- CreatureAnimator Tend switches on FolkJobs.Of(s): Knead, TendBody, Haul, LookAbout + spear for guards.
- CreatureRenderSystem GearParts 8. All derived from job/task; a client sees the task-implied job. Rule 11 holds.

## Shot flags (A-34)
- `-walk x,y,z` (logs position, to go, height over floor, biome, PushedTotal every 0.25 s), `-untilwork S`,
  `-watch *:Job`. Plus -place, -goto, -view, -camrel, -watchfrom h,d, -nopushout, -time, -weather, -nohud,
  -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -island v1.

## Cave collision and camera (A-30/32)
- PushOutOfSolids runs only in caves, only on a moving player's step, gated by NoPushOut.

## NEXT
- Finish A-35 rows 2-3 with the existing build (no rebuild needed unless the gesture still doesn't read).
- Close-camera fade before shot; fords (0 found).
