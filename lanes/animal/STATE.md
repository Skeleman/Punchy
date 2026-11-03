# Animal lane: state (2026-11-02, A-40 done)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin b2b6a70 (A-40) on e75918e (A-39) on 90e9211
(A-38). Lane hashes (player log, A-40 build): v2 126 / 053C63D5 (no world code touched).
Untracked `Assets/_Recovery/` is old and not ours. `Temp/PlayArgs.txt` holds an old a29b line (editor Play mode
reads it as extra flags); swap it out only for an editor shot run and put it back.
Editor: ports are swapped (7800 reaches Punchy-world). Target the animal editor by --pid: the Unity.exe whose
-projectpath is the Punchy tree (Win32_Process command line). Check `Application.dataPath` first. Play mode in the
animal editor sometimes exits by itself: re-enter before an eval that needs the world.

## Slope + wall push-out (A-39/A-40, PlayerMovementSystem.PushOutOfSolids)
- Grounded on a walkable cave slope (DownhillOnWalkableSlope), each sphere's wall push loses its downhill part.
- Not climbing and chest/head (BodyDepth) over 0.08 m in and deeper than at from: the body stays at from.
- Climbing (A-40): over 0.10 m in and deeper than at from, push sideways across the climb (DeepNormal, at most 2r).
  No sideways way out and the floor 0.3 m ahead doesn't rise: hold at from; else take the largest part of the step
  that stays out. A push back down the climb from a clear spot holds instead (stopped a diag -1 corner shiver).
- Sim: Temp/a40_sim.cs (eval in Play mode; wait for "h 102.8896"). Results: diag -1 0.10, seg 245 up 0.10 (climb
  2.25 of 2.43), other walks unchanged. Diag -1 now stops at the corner (progress 0.76, was 3.80).

## Warp keys (A-36)
- F5 biome, F6 settlement, F7 cave, F8 landmark; Shift back. `-place Start` returns to the spawn.
- Counts v2: biomes 35, settlements 6, caves 37, landmarks 126.

## Shot flags
- `-warp`, `-walk x,y,z`, `-untilwork S`, `-watch *:Job`, -place, -goto, -view, -camrel, -camlook, -watchfrom h,d,
  -nopushout, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -logFile.
- A shot-list line without -shot still saves `shot-N.png`; delete those.
- Slope 245 frame (walk up only): `-goto -1448.0,83.2,238.7 -walk -1448.4,86.2,241.1 -camrel 2.10,3.41,-0.44,-0.20,2.91,1.20`.
- Sideways-walk side frame (A-40, body visible): `-goto -1448.2,84.7,239.9 -walk -1444.26,84.7,240.56
  -camlook -1446.2,85.8,237.7,-1446.6,84.4,240.2 -wait 3.5`.
- Editor shots: put `-shotdir Temp/X -shotlist Temp/X.txt` in Temp/PlayArgs.txt and enter Play mode.
- Build: eval Temp/a40_build.cs with --timeout 600, after leaving Play mode.
- 800 px copies: Python PIL thumbnail.

## NEXT
- None open from A-40. Possible: diag -1 now stops dead at the corner instead of sliding along; if that feels
  sticky in play, try a slide along the wall that keeps the 0.10 m limit.
