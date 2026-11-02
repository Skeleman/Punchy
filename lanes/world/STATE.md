# World lane state

Updated 2026-11-02 after W-24. Branch world-features @ 9568761 (pushed). One build (Builds/Dev).

## Done in W-24
- Row 1 (#23, stepped pools): ponds whose bed is above sea level now terrace (`TerrainSource.Terrace`).
  - The bed snaps to 0.6 m bands; each band is a level pool behind a bank about 0.15-0.6 m high.
  - The pond grid labels on the pre-terrace dig (`PondDig`), so banks don't split ponds and the mask doesn't drop them.
  - v2 swamps use pond density 1.2x and cover +0.3. Pools stay off settlement streets and plots (`Keep`).
  - PoisonSwamp box (+-480 m at -2400,500), before -> after:
    - pools (16 m): 3 -> 15
    - wet cells: 15 -> 33
    - steps (distinct water levels): 2 -> 119
    - water area: 26 -> 1906 points of 2 m
  - Island-wide: 1 pond / 15 wet cells -> 5 ponds / 41 wet cells.
- Row 3: -2165,255 shot again from lower down with the lake in frame. No vertical walls and no holes in the lake.
  One ragged bank edge at the bottom right of the near shot.
- Row 4: hut at 40 m in the build. The thatch is even on every roof (W-23 fix holds).

## Open from W-24
- Row 2: NOT DONE. The build shot from 30 m up doesn't clearly read as stepped pools.
  - The pools are small: a 0.6 m step on a slope of about 0.1 gives terraces about 6 m wide.
  - From 30 m they show as pale flat patches and mud lines in the water, not as clear paddies.
  - Next:
    - a bigger TerraceStep (about 1.2 m) or a stronger bank so each terrace is wider and the rim shows;
    - or frame the shot lower and closer on the 11-cell pond at (-2260,569).
    - Measure in the editor, then make one build.

## Hashes (confirmed in the W-24 build log and editor)
v2 125 / AFA9CC63, caves D531293D622E785A; v1 101 / E128CB1D. Unchanged.

## Notes
- Shots: PunchyMilestones/world/w24 (800 px), raw shots in w24/raw.
- Scratch eval scripts: Punchy-world/Temp/w24_*.cs. w24_measure.cs gives the box numbers (run it in play mode).
- Biggest v2 ponds: 17 cells at (-2347,542), 11 at (-2260,569), 5 at (-2149,-3269).
