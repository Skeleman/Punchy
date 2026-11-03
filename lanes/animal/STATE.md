# Animal lane: state (2026-11-02, A-39 done in part)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin e75918e (A-39) on 90e9211 (A-38) on d17f6a7
(A-37). Lane hashes (player log, A-39 build): v2 126 / 053C63D5 (v1 102 / 51EA6463 from A-36; no world code touched).
Untracked `Assets/_Recovery/` is old and not ours.
Editor: ports are swapped (7800 reaches Punchy-world). Target the animal editor by --pid: the Unity.exe whose
-projectpath is the Punchy tree (Win32_Process command line). Check `Application.dataPath` first.

## Slope + wall push-out (A-39, PlayerMovementSystem.PushOutOfSolids)
- Grounded on a walkable cave slope (DownhillOnWalkableSlope: rise 0.1..SteepRise over SlopeSpan, all four probes
  in cave), each sphere's wall push loses its downhill part and is rescaled to keep its depth (capped at 2x).
- Not climbing and chest/head (BodyDepth) over 0.08 m in and deeper than at from: the body stays at from.
  Allowing this while climbing (0.10 m) stopped seg 245 up at 0.86 m, so it was reverted.
- Steep slide-back only where SteepThere(from) is true; at a wall's foot the body just stops. This fixed the side-1
  jitter, which slid back on every other frame.
- Sim: Temp/a39_sim.cs (eval in Play mode; wait for "h 102.8896" before trusting it). It prints travel, the real
  path length; "pushed" mostly cancels each step into the wall (~0.067 m/frame).
- Open: diag -1 sinks 0.19 (0.14 before), seg 245 up sinks 0.14 (0.10 before), both while climbing.

## Warp keys (A-36)
- F5 biome, F6 settlement, F7 cave, F8 landmark; Shift back. The first press, or one more than 300 m from the last
  warp, sorts the list nearest the player first. `-place Start` returns to the spawn (0, 0) before a warp line.
- Counts v2: biomes 35, settlements 6, caves 37, landmarks 126. Town 3 from biome 1 = DwarfMiningCamp #6.

## Shot flags
- `-warp`, `-walk x,y,z`, `-untilwork S`, `-watch *:Job`, -place, -goto, -view, -camrel, -camlook, -watchfrom h,d,
  -nopushout, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -island v1, -logFile.
- A shot-list line without -shot still saves `shot-N.png`; delete those.
- Slope 245 frame: `-goto -1448.0,83.2,238.7 -walk -1448.4,86.2,241.1 -camrel 2.10,3.41,-0.44,-0.20,2.91,1.20`.
- Build: eval Temp/a38_build.cs (BuildPipeline.BuildPlayer) with --timeout 600.
- 800 px copies: Git Bash has no ImageMagick; use a Python PIL thumbnail.

## Shot flags
- `-warp`, `-walk x,y,z`, `-untilwork S`, `-watch *:Job`, -place, -goto, -view, -camrel, -camlook, -watchfrom h,d,
  -nopushout, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -logFile.
- A shot-list line without -shot still saves `shot-N.png`; delete those.
- Slope 245 frame (walk up only): `-goto -1448.0,83.2,238.7 -walk -1448.4,86.2,241.1 -camrel 2.10,3.41,-0.44,-0.20,2.91,1.20`.
  On the sideways walk (goto -1448.2,84.7,239.9, walk to -1444.26,84.7,240.56) this frame faces rock and
  shows no body. Find a side frame in the editor first.
- Build: eval Temp/a39_build.cs with --timeout 600, after setting isPlaying = false.
- 800 px copies: Python PIL thumbnail.

## NEXT
- Sink while climbing near a wall: diag -1 0.19, seg 245 up 0.14. Needs a fix that leaves the climb working.
- Retake the sideways-walk shot with a frame that works (after shot only; the old build is gone).
