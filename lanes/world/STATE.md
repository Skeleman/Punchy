# World lane state

Updated 2026-11-02 after W-27. Branch world-features @ fba3a7f (pushed). One build (Builds/Dev).

## Done in W-27 (salt tower)
- New landmark type SaltTower (`OnePerIsland`, `TowerHeight` 76 m, +-10% per island). It is listed at
  density 0 in SaltFlat, so it never rolls with the other landmarks.
- `SaltTowerBaking.Place` runs after roads and roadside landmarks and is appended last. Types() also puts
  one-per-island types last, so no other index or landmark moves.
  - Spot: the deepest point of the largest salt-flat patch (40 m grid, weight >= 0.75, dry). Relief must be
    0.15 m or less, it must be clear of every landmark footprint + 40 m, and at least 300 m from settlements.
    Roads are not checked.
  - v2: 3720,-800, height 73.4 m above base 7.0, 245 steps of 0.300 m, 280 m in from the patch edge.
  - v1: 3080,320, height 69.9 m, 234 steps.
- `SaltTowerShape` is analytic: a body of radius 7.5, steps at radius 8.4 (a 1.05 m standing circle, drawn
  as a 2.3 m hex), two straight tunnels through the body, and a summit slab of radius 8.
  - `TerrainSource.FloorUnder` stands bodies on the steps and summit.
  - `TerrainSource.TowerWall` and `PlayerMovementSystem.KeepOutOfTower` keep the player out of the column
    and out of steps from below.
- `SaltTowerSystem` (presentation, solo and client) draws 9 jittered hex columns, split into blocks, with
  the tunnels cut out. LODs: LOD0 7048 tris (< 250 m), LOD1 1660 (< 1200 m), LOD2 692.
- Walker (`CreekProbe2.TowerWalk` in the untracked probe): 2336 moves of 0.1 m from the ground to the
  summit centre. Max rise 0.339 m against a 0.4 step up. Max drop 0, blocked 0, top reached True.

## Open
- The far shot from 400 m west looks over Sonoran scrub: the patch is only 0.4 km2, so 400 m out is off
  the flat. The tower reads as small and grey from there, and rain was falling in all three shots.
- The summit is a plain slab. A rim or cairn could be added if Zach wants one.
- The column body uses the summit collision height everywhere, so the drawn blocks and the collision
  match only roughly at the tunnel mouths.
- Caves hash not re-run (caves bake after the tower; the tower only adds a landmark footprint).

## Hashes
- v2 125 / D709D95C -> 126 / 053C63D5. Diff against lmlist-w26v2: only `+ SaltTower 3720.0 -800.0`.
- v1 101 / E128CB1D -> 102 / 51EA6463. v1 has salt flats. Diff against lmlist-w25v1: only
  `+ SaltTower 3080.0 320.0`.

## Notes
- Shots: PunchyMilestones/world/w27 (800 px), raw shots in w27/raw.
- Scratch: Punchy-world/Temp/w27_*. Shot list: w27_shots.txt. Player log: w27player.log.
- The world editor logs to Punchy-world/Temp/editor-w04.log, not the default Editor.log.
- The v1 island bakes in an import worker; its SaltTower log line carries a `[Worker2]` prefix there.
