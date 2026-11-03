# Animal lane: state (2026-11-02, A-37 done)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin d17f6a7 (A-37) on f82be16 (A-36) on 4c03cde
(A-35). Lane hashes (player log, A-37 build): v2 126 / 053C63D5 (v1 102 / 51EA6463 from A-36; no world code touched).
Untracked `Assets/_Recovery/` is old and not ours.
Editor: during A-37, port 7800 reached the WORLD editor (Punchy-world). The animal editor (D:/Unity/Punchy) answered
on port 7801 and by --pid. Check `eval Application.dataPath` before any editor call.

## Push-out on cave slopes (A-37, PlayerMovementSystem.PushOutOfSolids)
- Cause: seg 245 is lumpy (a ~70 deg ledge, then 52-62 deg). The steep-floor test read ONE SDF normal at the foot
  (32-79 deg along the walk), so every reading over 50 deg undid the step and slid the body back at 3 m/s. The knee
  sphere was pushed off the floor itself, and the mid/head spheres (d 0.21 < r 0.35 at the ledge, normals pointing
  downhill) shoved it back.
- Fix: steep = rise over a 2 m span (FloorAlong, 4 FloorUnder steps each way) > 60 deg (SteepRise 1.73); standing,
  the knee sphere skips floor normals (n.y > 0.1); climbing a walkable slope, push-out never pushes back along the climb.
  Wall/overhang pushes, the 2r cap and the head-ceiling revert are unchanged.
- Sim (Temp/a37_sim.cs, real PushOutOfSolids): seg 245 up progress 2.26/2.43 m, pushed 0.47 (before: -1.98 m,
  pushed 20.9); down 2.27, pushed 0.20 (before: 2.88 m pushed in 0.23 s); flat seg 0 pushed 0.03, progress 3.80/4.00
  (same as push off). maxPen 0.10 (push off 0.18).

## Warp camera (A-37)
- ThirdPersonCamera, open ground only (CaveDistance(head) >= CaveNear): 16 ground samples on the target->camera line;
  the first one under ground+0.25 m pulls the camera in to the sample before it (min 1.5 m).
- Warp sets Distance <= 5 m (WarpCameraDistance) and pitch 8.
- Gap: trees are not in TerrainSource. v2 town 3 (DwarfMiningCamp) shows the player, but the camera sits in a tree crown.

## Warp keys (A-36)
- F5 biome, F6 settlement, F7 cave, F8 landmark; Shift back. The first press, or one more than 300 m from the last
  warp, sorts the list nearest the player first.
- So `-warp X N` depends on where the player is: to retake earlier warp shots, put the warp lines first after load.
- Counts v2: biomes 35, settlements 6, caves 37, landmarks 126. Settlement names are landmark type + index.

## Shot flags
- `-warp`, `-walk x,y,z`, `-untilwork S`, `-watch *:Job`, -place, -goto, -view, -camrel, -camlook, -watchfrom h,d,
  -nopushout, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -island v1, -logFile.
- Slope 245 frame: `-goto -1448.0,83.2,238.7 -walk -1448.4,86.2,241.1 -camrel 2.10,3.41,-0.44,-0.20,2.91,1.20`.
- Build: eval BuildPipeline.BuildPlayer with --timeout 600.

## NEXT
- Warp camera vs tree crowns: test flora/canopy along the camera line (FloraSupport or tree instances), or skip warp
  targets under canopy.
- Optional: check the climb rule against walking into a wall while on a slope (the move step's clear check still blocks it).
