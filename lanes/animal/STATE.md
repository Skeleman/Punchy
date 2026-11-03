# Animal lane: state (2026-11-02, A-41 code done, shots + build BLOCKED)

Worktree `Punchy` (animal lane only). On `animal-behaviours`: local commit e3b8521 (A-41, NOT pushed: no shots yet)
on origin b2b6a70 (A-40) on e75918e (A-39). Lane hashes (A-40 build): v2 126 / 053C63D5 (A-41 touches no world code).
Untracked `Assets/_Recovery/` is old and not ours. `Temp/PlayArgs.txt` holds an old a29b line (editor Play mode
reads it as extra flags); swap it out only for an editor shot run and put it back (restored after A-41).
Editor: ports are swapped (7800 reaches Punchy-world). Target the animal editor by --pid (26472 on 2026-11-02;
the Unity.exe whose -projectpath is the Punchy tree). Check `Application.dataPath` first.

## A-41 humanoid run pose (in e3b8521)
- Run pose = limb keys (Assets/Game/Fauna/Limbs/HumanArm, HumanLeg) through CreatureAnimator (animal lane).
  HumanArm: Human Elf Dwarf Ogre Hermit Goblin Ghost. HumanLeg: all but Goblin (GoblinLeg) and Ghost.
- Elbow: run keys 85/95 deg; CreatureAnimator holds upright elbows at the keys when running (arm swing trait and
  sprint pump no longer scale it). Before: run 42-112 by trait, sprint 149 (clamp). After: 85-95 everywhere.
- HumanLeg RunPush hip 48 -> 32, RunReach -54 -> -60. Hip extension run 48 -> 32, sprint 65 -> 43; run stride
  2.648 -> 2.423 m (cadence follows from MeasureGait, no slide). GoblinLeg untouched (15 deg).
- Probe: Temp/a41_angles.cs (eval; tag in Temp/a41_tag.txt before/after) -> Temp/a41_angles.txt.
- BLOCKED: Zach's Punchy.exe (Builds/Windows, PID 34888, from 20:47) ran the whole batch (waited 20:59-21:31).
  No before shot, no build, no after shots in the player yet.

## To finish A-41 (next agent, when no Punchy.exe runs from Builds/Windows)
1. Before: run the current Builds/Windows exe with Temp/a41_shots.txt
   (`-shotlist Temp/a41_shots.txt -shotdir <dir> -quit -logFile ...`). Its last lines are run-2.0..2.3.
2. Build (Temp/a40_build.cs, --timeout 600, out of Play mode), same list for after; check log hash 126/053C63D5.
3. Pick the mid-stride pair (editor pick: run-2.0 / run-2.2 show the trailing leg), 800 px, push e3b8521.
- Frame: `-goto -255,27 -wait 1.5` line, then `-walk -205,35.4,27 -camrel 12,1.0,-6,12,0.9,0 -wait 2.0`. The ground
  there rises behind the runner (dirt background); camlook with y 35-36 is underground there now.

## Warp keys (A-36)
- F5 biome, F6 settlement, F7 cave, F8 landmark; Shift back. `-place Start` returns to the spawn.

## Shot flags
- `-warp`, `-walk x,y,z`, `-untilwork S`, `-watch *:Job`, -place, -goto, -view, -camrel, -camlook, -watchfrom h,d,
  -nopushout, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -logFile.
- A shot-list line without -shot still saves `shot-N.png`; delete those. -camrel is relative to the player at
  line start: put the -goto on its own line first so the player has landed.
- Editor shots: put `-shotdir Temp/X -shotlist Temp/X.txt` in Temp/PlayArgs.txt and enter Play mode.
- Editor walk-probe log lines don't reach %LOCALAPPDATA%/Unity/Editor/Editor.log for this editor.
- 800 px copies: Python PIL thumbnail.

## NEXT
- Finish A-41 shots + build (above), then push e3b8521.
- From A-40: if diag -1 feels sticky at the cave corner, try a wall slide that keeps the 0.10 m limit.
