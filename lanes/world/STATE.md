# World lane state (after W-32, 2026-11-02)

Branch world-features, head 1320e8f (W-48, W-52, W-53, W-32 local, not pushed). Worktree D:/Unity/Punchy-world.

## Hashes (editor play mode, Temp/w37_measure.cs, tag w53e)

| | v2 | v1 |
|---|---|---|
| landmarks | 127 / 4D245E12 (W-32 temple added) | 103 / 19885131 |
| caves | C49E8E3036C741FC | 47C703C0E17F77CB |
| rivers | A5B3DE5D53257332 | 2508BF538DA96D7E |
| ground | F8F938002B0CBE46 | A32E650C44A45E06 |

Landmarks are unchanged since W-36. W-53 moved v2 rivers, ground and caves (meanders); the old W-48 values were
caves ACA9CC9155621A44, rivers 70B01EF403A78FF5, ground A87024EE3525ADC0.

## W-48 / W-52 / W-53

- W-48 cave perf: camera arm probe is Burst; deep in a cave only the camera's cave chunks draw (SurfaceHidden >= 0.99).
  Still unexplained: ~33 ms frames for ~70 s after load. Shot switches `-perflog`, `-gocave x,z|#n`, `-oldcave`,
  `-nocavewalls`.
- W-52: still water's mesh reaches over shallow beaches (mesh only, no height change).
- W-53 (ef41bf2): W-37 river meanders merged, scaled by river width; landmarks are placed on the jittered river map
  (list unchanged); the v2 34.51 m dam is cut as a canyon instead of filled; wet sand reaches round, not square.
- ArcSpire prongs collide as stacked slices; shooting stars 1/20 (`-redstar`); Colossus/Mantrap winding fixed.

## W-32

- SunkenTemple (1320e8f): one per island with dunes, v2 3520,360 and v1 1320,-2720, placed in a second pass after towers
  (salt 0x5E7A). 8 rooms on 3 levels with analytic collision (SunkenTempleShape) through Deck/TowerWall/FloorUnder.
  The interior draws at LOD0 only with the camera inside. Design note: docs/agents/interiors.md.
  Measure: Temp/w32_temple.cs (log, tris, walk test, shot coordinates).

## Open ideas (not scheduled)

- Lake backwater over low banks: lake surface reaching up a river past where its banks are lower than the lake.
- Unexplained v2 water steps (listed in the W-36 report; not recounted after W-53).

## Tools

- Temp/w37_measure.cs: hashes (landmarks/caves when Temp/w37_full.txt exists), steps, open ends. Tag in
  Temp/w37_tag.txt. Island from PUNCHY_ISLAND in the editor env.
- Untracked Assets/Temp/Editor/WorldProbe.cs (leave alone): LmList, CaveBench, Shot, TowerWalk.
