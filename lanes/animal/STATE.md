# Animal lane: state (2026-11-02, A-36 done)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin f82be16 (A-36 warp keys) on 4c03cde (A-35)
on bf170f2 (merge of main 41a9259). Lane hashes (player log, A-36 build): v2 126 / 053C63D5, v1 102 / 51EA6463.
Untracked `Assets/_Recovery/` is old and not ours. Editor on port 7800.

## Warp keys (A-36)
- DebugControls F5 biome region, F6 settlement, F7 cave entrance, F8 landmark; Shift+key back. F5-F8 were free
  (F4 is bound in InputSystem_Actions). First press (or after moving 300 m off the last warp) sorts nearest first.
- Lists built in FindWarpTargets beside the N list (same v1->v2 invalidation), read only from TerrainSource:
  biome regions = flood fill of the 150 m survey (4+ cells, deepest cell); settlements from Settlements blob (on the
  main road just outside Radius/Palisade); caves from Caves blob (6 m out from Mouth, away from node 1); landmarks
  from every instance (as N). Stand points walked to dry land (Dry / WalkToDryLand). Places like Jump.
- Counts: v2 biomes 35, settlements 6, caves 37, landmarks 126; v1 biomes 45, settlements 12, caves 17, landmarks 102.
- Banner top centre 3 s (shown even with -nohud): "F6 settlement: Name 3/6 (x, z)". Help line has a warp row.
- Shot flag `-warp biome|town|cave|landmark N` (Nth from nearest). Log: "DebugControls: warp keys ...; targets: ...".
- Gap: settlements have no names in the blob, so they are named by landmark type + index ("HumanVillage #4").
- Framing: the follow camera can sit inside a steep slope (biome 1 Mountains shot) or under canopy (v2 town 3).

## A-35 results
- Slope seg 245: -nopushout walks up; push-out slides the body 4 m back down (16.7 m in 2 s). Use
  `-camlook -1445.9,86.6,238.3,-1446.9,81.5,237.8` or `-view -10,20` to keep the body in frame.
- Merchant front-on: `-watch *:Merchant -watchfrom 1.6,2.5 -untilwork 60 -wait 8`.

## Shot flags
- `-warp`, `-walk x,y,z`, `-untilwork S`, `-watch *:Job`, -place, -goto, -view, -camrel, -camlook, -watchfrom h,d,
  -nopushout, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -island v1.
- Hashes: `[island] generator version N ... <n> landmarks, hash <X8>` line in the player log.
- Build: eval BuildPipeline.BuildPlayer with --timeout 600; quote strings as \" inside a single-quoted PS arg.

## Open
- PushOutOfSolids pushes a player back down a real 55 deg cave slope (seg 245): flagged for the supervisor.

## NEXT
- Waiting for the next card. Possible: warp camera pull-in on steep slopes; settlement names if the world lane adds them.
