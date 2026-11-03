# Animal lane: state (2026-11-02, A-38 done)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin 90e9211 (A-38) on d17f6a7 (A-37) on f82be16
(A-36). Lane hashes (player log, A-38 build): v2 126 / 053C63D5 (v1 102 / 51EA6463 from A-36; no world code touched).
Untracked `Assets/_Recovery/` is old and not ours.
Editor: ports are swapped (7800 reaches Punchy-world). Target the animal editor by --pid: the Unity.exe whose
-projectpath is the Punchy tree (Win32_Process command line). Check `Application.dataPath` first.

## Warp camera vs trees (A-38, DebugControls.ClearWarpView / ClearArm)
- After a warp it tries the landing heading, then 30 deg steps either way (up to 180). The first side whose
  head->lens line misses crowns and trunks wins. Crown margin: CrownRadius + 0.5 m, or 1.2 m over the last 1.5 m by
  the lens. Plants come from FloraPlacement.Place (layers 1-2, read only). If no side is clear, it takes the
  clearest one and shortens the arm (logged).
- The lens line is tested where the lens really ends up: lifted to ground + 0.5 m, as ThirdPersonCamera lifts it.
  Without that (first try), the DwarfMiningCamp lens was lifted from the 8 deg line into the crowns on the uphill side.
- One log line per warp: `warp camera clear of crowns, turned N deg, arm A m, lens H m over the head`.
- DwarfMiningCamp: turned 120 deg, lens 4.2 m up, no leaves. The other warps from Start turned 0 or 30 deg.

## Climb rule regression (A-38 sim, Temp/a38_sim.cs in the editor's Play mode, real PushOutOfSolids)
- No tunnelling with push-out on. Into the wall from the seg 245 slope: maxPen 0.01-0.03 sideways, 0.07 and 0.14
  diagonally. Ledges: 0.00. With push-out off, the same walks tunnel up to 4.75 m.
- Found: walking sideways into the wall on the slope, push-out jitters (pushed 13.5-20.8 m in 3 s) and the body slides
  ~1.4 m down to the slope's foot. One diagonal walk has maxPen 0.14 (over A-37's 0.10).

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

## NEXT
- Slope + wall jitter: walking sideways into a cave wall on seg 245, push-out alternates with the move step and
  slides the body down (the wall pushes have a downhill part). Maybe drop the downhill part of a wall push while
  grounded on a walkable slope; recheck the diagonal maxPen 0.14.
