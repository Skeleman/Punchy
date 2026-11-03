# World lane state

Updated 2026-11-02 after W-28b. Branch world-features @ fd91300 (pushed). One build (Builds/Dev).

## Done in W-28b (less uniform icebergs)
- Each berg gets a seeded shape class from its own stream:
  - Big: tabular, dry dock (two peaks), dome, tilted, pinnacle, capsized.
  - Chunks and floes: tabular, tilted, pinnacle, dome.
- Also per berg: a stretched footprint (up to 1.8:1, or 2:1 small), random yaw and a tilted top, an off-centre peak or
  crown, uneven facets, a notch (55% big, 30% small), an undercut (half the slab-like big bergs) and a waterline ledge
  (40% big). The ledge sits at 0.18 m, so it is swum onto.
- No two neighbours alike: a class is skipped if the nearest placed berg has it, and a second pass fixes a later
  neighbour where the new class still fits its room. v2: 1 of 100 bergs still shares a class with its nearest.
- Collision (TerrainSource.Deck/TowerWall via IcebergShape.Height) and the meshes sample the same shape functions.
- Colour: a 64x1 palette strip set on a copy of IcebergIce. White snow tops, bluer or greyer faces per berg (Tint),
  jitter per face, deeper blue underwater, and a dark blue streak on 35% of big bergs.
- The cave berg stays a slab so the chamber keeps its roof: stretched 1.05-1.3, tilt 0.04, no notch or ledge.
- Cave joint: a double-sided frame at the chamber wall, from the tube's arch out to a 5.4 x 4.3 m arch, closes the gap.
- Tower steps: TerrainSource.Deck now also returns SaltTowerShape.Floor. PlayerMovementSystem was not changed.

## Checks
- Shapes, v2: big 6 [tabular 3 (incl. cave), dry dock 2, dome 1], small 94 [tabular 26, tilted 27, dome 21, pinnacle 20].
- Cost:

  | Island | Before | After |
  |---|---|---|
  | v2 | 5 draws, 6232 tris | 5 draws, 6712 tris |
  | v2 cave | 1026 tris, 1 draw | 1058 tris, 1 draw |
  | v1 | 3 draws, 2140 tris | 3 draws, 1420 tris |

  v2 counts: big 6, chunks 64, floes 30. They were 6 / 56 / 38: the berg counts moved, which the card allows.
- Cave walk: 958 steps, 0 blocked, reached the chamber and came back.
- Tower walk: the editor called the player's own Floor through reflection. Tower (3720,-800), 245 steps, 2196
  sub-steps, 0 blocked, from floor 7.64 to the top step at 80.42. Before the fix the ground there read 6.99.

## Open
- The cave berg, which the W-28 shots centre on, is still a near-round slab. Most of the change is in the other
  bergs and in the colour. A taller dry-dock cave berg (top at least 13 m over the chamber) is possible.
- No shot shows the other big bergs close up (pinnacle, dry dock and dome views were not in the card's list).
- v1 shape counts were not logged (the static log stayed from v2). Only v1's mesh stats and hashes were read.
- From W-29: a slanted water sheet beside the main falls curtain, snowy levees and plain gorge walls.

## Hashes (unchanged in W-28b)
- v2 126 / 053C63D5, caves 5D90D9CFC5326976.
- v1 102 / 51EA6463, caves 47C703C0E17F77CB.

## Notes
- Shots: PunchyMilestones/world/w28b (800 px), raw shots in w28b/raw.
- Scratch: Punchy-world/Temp/w28b_*. Tower walk: Temp/w28b_tower.cs. Hashes and logs: Temp/w28b_hash.cs.
- Editor console errors: Temp/w28b_errs.cs (LogEntries via reflection).
- Shots: run `Builds/Dev/Punchy.exe -shotlist <file> -shotdir <dir> -logFile <file>`. A `-quit` line ends the list.
