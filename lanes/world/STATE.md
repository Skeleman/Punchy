# World lane state (after W-42, 2026-11-02)

Branch world-features, head f137084 (pushed). W-37 river patch parked on w37-wip (not merged). Worktree D:/Unity/Punchy-world.

## Hashes (editor play mode, Temp/w36_measure.cs)

| | v2 | v1 |
|---|---|---|
| landmarks | 126 / 053C63D5 | 102 / 51EA6463 |
| caves | ACA9CC9155621A44 | 47C703C0E17F77CB |
| rivers | 70B01EF403A78FF5 | 2508BF538DA96D7E |
| ground | A87024EE3525ADC0 | A32E650C44A45E06 |
| falls | 70 | 0 |

Player v2 log (Builds/Dev, W-36 build): 126 landmarks, hash 053C63D5.

## W-42

- ArcSpire prongs collide as stacked slices (FloraModel.ColliderBase), within 14 m only. Shooting stars 1/20, 1 in 6
  red-orange; `-redstar` holds one for shots. Shots 3 and 5 still owed (W-42 shots failed).

## Rivers now

- W-36 `RiverBaking.FillRiverPits` (after ReshapeLoweredLakes): a bed below the bed it drains into is raised under its
  backed-up water. Lake and sea backwater untouched. Water levels unchanged.
- Rapids foam: `RiverQuery.Gradient` is the smaller of bed and water drop; lake-outlet foam scales with the real ramp.
  The grove-lake white strip (W-35b shot 4) was this fault and is gone.

## Open river ends (edge rule), not at falls

- v2: 92 (was 106). Cut by another carve 38 (rapid/junction banks lowered by a neighbouring segment), low ground
  beside 32 (river held at a dam level with land beside lower: -2260..-1955,-358..-205 at 34.51 m and
  947..1388,2267..2643), backwater 22 (lake backwater over low banks: lakes at 28.49 m near 390..710,1050..2240;
  -2196,3715 is sea). Full list in REPORTS-world.md W-36.
- v1: 3 (unchanged): 2424,1000 and 1547,3277 cut by another carve; -2315,1571 backwater.

## Steps > 0.3 m (cells by reason)

- v2: 117 cells: cascade 45, rapid (bed drop > 1 m over two cells) 30, gorge rapids 19, lake/sea shore 9, two
  rivers side by side 7, unexplained 7 (was 36 by the same rule, 44 by the W-35b rule).
- v1: 32 cells: gorge 16, side by side 9, rapid 4, confluence 3, unexplained 0 (was 13).

## Open ideas (not scheduled)

- The 34.51 m dam on v2 (-2343,-437 chain): cutting the dam instead of filling would remove the low-ground-beside
  ends there, but carves a ~15 m canyon for ~500 m. Needs a decision.
- Lake backwater over low banks: lake surface reaching up a river past where its banks are lower than the lake.
- 7 unexplained v2 steps (0.33-1.34 m), listed in the W-36 report.

## Tools

- Temp/w36_measure.cs: hashes, steps with reasons, open ends with reasons, grove line. Tag in Temp/w36_tag.txt;
  Temp/w36_full.txt switches on the landmark/cave benches. Island from PUNCHY_ISLAND in the editor env.
- Untracked Assets/Temp/Editor/WorldProbe.cs (leave alone).
