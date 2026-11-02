# World lane state

Updated 2026-11-02 after W-23 (partial). Branch world-features @ 253080d.

## Done in W-23
- Row 3 (hut roofs at the middle distance): not an LOD or shadow bug. The bark relief (cave detail texture,
  albedo plus a strong normal bump) faded over 18-45 m, so a thatch roof became a plain cone whose facets
  showed at 30-55 m. That is also where huts switch to LOD1 on the Low view preset (View 0.6, LOD1 about 33 m).
  Fix: built things fade that relief over 60-140 m (Flora.shader). Shader only, hashes untouched.

## Open from W-23
- Row 1 (#23, denser v2 swamp pools): tried and reverted, nothing committed.
  - Tried a v2-only pond noise density of 1.6x with +0.15 cover, plus dropping pond cells on slopes or under
    settlement levelling.
  - Finding: the v2 PoisonSwamp around (-2400,500) is a hillside. Its median pre-pond slope is 0.16 m/m over
    16 m, and 976 of 1101 cells are above 0.12.
  - Result: the slope rule removes almost every pool (0.12 left 0 pools; 0.22 left 15 single-cell specks,
    all under PondMinCells). Before the change: 1 pool, 15 wet cells island-wide.
  - Options:
    - (a) flatten the swamp's base height in v2 (a biome or shape change, so it may move hashes; warn first);
    - (b) allow pools on slopes and let the pond dig level them (terraced pools);
    - (c) place the swamp on flatter ground in the v2 map.
- Rows 2 and 4: no build was made (the call budget ran out after row 1). Both still need the build shots:
  - Row 2: the swamp from about 30 m up.
  - Row 4: -2165,255 lower down, with the lake in frame.

## Hashes
v2 125 / AFA9CC63, caves D531293D622E785A; v1 101 / E128CB1D. Nothing terrain-side was committed.

## Notes
- Shots: PunchyMilestones/world/w23 (800 px).
- Scratch eval scripts: Punchy-world/Temp/w23_*.cs.
