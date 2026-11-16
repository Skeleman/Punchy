# Unmerged branch triage (11-15)

Every unlanded branch on origin, checked with merge-tree against origin/main on 11-15. Only chemistry-* is left out (Zach: not this project). The first pass covered 36 branches and missed the 11-13 to 11-15 ones.

## Merged 11-15 (catch-up)
far-towns #373, lamplighter #374, notice-board-life #362, fire-heat #375, door-sound #376, scarps #377, level-gardens #378,
cave-entry #379, no-water-discs #380, puddle-edges #381, swell-noise #382, cliff-kit #386,
individual-builds #387, style-parity #388, folk-pets #389, folk/playtest-1114 #390, spawn-wait-fix #391, cloth-legs #394,
world-pack #407, swimmer-splash #408, render/playtest-1114 #409 (R-38 rows 0-4; branch continues), cloud-blocks #410, waterfall-sheets #411, folk/dialogue-archive #416, fauna/model-lab-2 #417, #418 (M-06 rows 1-3; branch continues), player/fishing #419 (P-33 rows 1-3), folk/dialogue-options #420 (A-131), fauna/dune-groves #421 (A-130), player/dismount #422 (P-40), render/far-lights #423 (R-25 r3, R-38), b/fix-swapchain-stall #424, lab/map-editor #425 (M-07 r1-2a; branch continues), world/material-library #426 (W-166 r2-3a; branch continues), render/stone-trial #427 (R-34 r2, squashed), fauna/far-life #428 (A-108 r2; holds fauna/lying-legs), fauna/playtest-1114 #429 (A-126 r2-4), fauna/one-population #430 (A-110), fauna/swimmer-droplets #431 (A-125 r2), render/playtest-1115 #432 (R-39), flora/tree-variety #433 (A-113).

## Land: the owning lane merges main in, re-pins, verifies, hands off "ready for PR"
| Branch | Card | Owner | Note |
|---|---|---|---|
| folk/a111-row1-parked | A-111 r1 | folk | Parked; only if A-110 slips. |
| water/truth | bench 75 | world | IslandV2, TerrainSource. |
| world/log-bark | W-170 r1 | world | Clean; confirm verified. |
| world/ambient-details | W-142 | world | Job 69 FAIL at lava cave: fix seeps first. |
| world/cave-trickles | W-141 | world | Job 68 FAIL: cheaper version. |
| world/far-lite | W-132 | world | Check overlap with R-22/R-23. |
| world/cave-climbs | W-144 | world | Waits on Zach's look at 1b pairs. |
| render/audit-fixes | R-15 r1-3 | render | WaterPatchSystem. |
| render/cobble-depth | R-28 r2 | render | Terrain shader vs puddle-edges. |
| render/one-surface | R-23 r2 | render | Doc only. |
| b/cloud-shadow-map | profiler | profiler | Rework: store raw density, apply R-20 r4 soft edge after the read; BENCH cost check. |
| player/netsim | P-31 | player | Merge main, re-check -netsim. |
| player/take-items | P-38 (#372) | player | Rows 1-2b DONE but never PR'd (11-15 audit): merge main in, hand off ready for PR. Holds unify-items, items-pick, items-axe, hair, clothing. |
| world/playtest-1114 | W-172 rows 2+ | world | In flight. |
| world/ground-edits | W-174 (#406) | world | In flight (design note). |
| render/foliage-realism | R-30 r1 (#323) | render | In flight. |
| folk/inn-job | A-132 (#25) | folk | In flight. |
| player/menus | P-39 (#397) | player-2 | In flight. |
| lab/set-piece-view | M-08 r2 (#414) | lab | In flight; verify when RAM allows. |

## Bench only (never merge)
b/bench, b/bench-lod, b/bench-stress, b/bench-job84, b/bench-job92b1.

## Stale, archive once Zach OKs
tmp/animal-slot6, test/playtest-1108, render/ssao-source, rc/v0.10.0, rc/v0.14.0.

## Folded into other work
- player/clothing, player/hair, player/unify-items, player/items-pick, player/items-axe: inside player/take-items (P-38); archive once it lands.
- player/armour, player/weapons: covered by take-items. player/tools: all but 32 lines; P-38 picks those up.
- player/seamless: P-37 row 3 builds on it.

## Superseded: archived and deleted 11-15 (Zach OK)
Each is kept as tag `archive/<branch>`. Owners check theirs on resume; restore with
`git push origin archive/<branch>:refs/heads/<branch>`.
- rc-test/* (8) and wip/* (43): archived and deleted 11-15 (Zach OK), tags archive/rc-test/*, archive/wip/*.
- render/shadow-bias-test (R-37): archived 11-15, Zach saw no difference; bias stays 0.1/0.5.
light/forwardplus, b/r10-trims, b/r10-nocluster (R-10, redone in far-lights work); lod/screen-size (ScreenSize is on main);
render/cloud-shadow-soft, shadow-still (R-20 row 2 parked); b/r12-trims, b/r12-warp-bake (looked wrong);
b/water-rows-fixes, b/fix-waterfield-step, water/fine-grid (water trials); refactor/pose-by-size, fix/salt-ball-far-shadow;
world/mouth-rim, dark-crystal, cave-walk-46s, player/cave-water-camera (small; each owner confirms).

## Fix for the future
Every DONE row opens a PR (draft if it waits on something), and the supervisor sweeps unmerged branches daily.
