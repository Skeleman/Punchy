# Unmerged branch triage (11-15)

Every unlanded branch on origin, checked with merge-tree against origin/main on 11-15. Only chemistry-* is left out (Zach: not this project). The first pass covered 36 branches and missed the 11-13 to 11-15 ones.

## Merged 11-15 (catch-up)
far-towns #373, lamplighter #374, notice-board-life #362, fire-heat #375, door-sound #376, scarps #377, level-gardens #378,
cave-entry #379, no-water-discs #380, puddle-edges #381, swell-noise #382, cliff-kit #386,
individual-builds #387, style-parity #388, folk-pets #389, folk/playtest-1114 #390, spawn-wait-fix #391, cloth-legs #394,
world-pack #407, swimmer-splash #408, render/playtest-1114 #409 (R-38 rows 0-4; branch continues), cloud-blocks #410, waterfall-sheets #411, folk/dialogue-archive #416, fauna/model-lab-2 #417, #418 (M-06 rows 1-3; branch continues), player/fishing #419 (P-33 rows 1-3), folk/dialogue-options #420 (A-131), fauna/dune-groves #421 (A-130), player/dismount #422 (P-40), render/far-lights #423 (R-25 r3, R-38), b/fix-swapchain-stall #424, lab/map-editor #425 (M-07 r1-2a; branch continues), world/material-library #426 (W-166 r2-3a; branch continues), render/stone-trial #427 (R-34 r2, squashed), fauna/far-life #428 (A-108 r2; holds fauna/lying-legs), fauna/playtest-1114 #429 (A-126 r2-4), fauna/one-population #430 (A-110), fauna/swimmer-droplets #431 (A-125 r2), render/playtest-1115 #432 (R-39), flora/tree-variety #433 (A-113), lab/set-piece-view #435 (M-08), b/fix-testhooks-guard #436, world/material-library #437 (W-166 to 439e8892; branch continues with the W-175 doc), folk/inn-job #438 (A-132), render/far-lights-2 #439 (R-25 r5-6), world/ground-edits #440 (W-174 r1 note; branch continues), player/ride-fit #441 (P-43), lab/new-species #442 (M-06 r3b), b/fix-dolphin-wake #443 (A-125), fauna/far-details #444 (A-108 r4), b/fix-audio-doors #445 (#352), player/netsim #446 (P-31), b/fix-lake-ring-hash #447, fauna/material-library #448 (A-133), b/fix-skybird-hash #449, player/take-items #450 (P-38), world/playtest-1114 #451 (W-172 r2-6), world/material-library-2 #452 (W-166 r3), #456 (temple ashlar), fauna/lab-herds #453 (M-06 r3b herds), render/one-surface #454 (R-23 r2), render/audit-fixes #455 (R-15 r1-3; render/audit-fixes-wip superseded), folk/gait #458 (A-124 r2), fauna/lab-colour #459 (M-06 r4), player/menus #434 (P-39), world/fix-259-rejoin #461 (#259), world/parts-units #462 (W-175 r2; branch continues), world/ashlands-lava #463 (W-172 lava), fauna/ride-size #464 (A-119), world/parts-units-2 #465 (W-175 r3), player/dungeon-pieces #466 (P-45 r1-3), b/fix-port-flag #467 (rule 35 -port), player/talk-replies #468 (P-42), fauna/lab-blueprint #469 (M-06 r5), fauna/dungeon-spawns #470 (#24 p2 r1 note; branch continues), render/stone-include #471 (W-166 r3 tail), player/p46 #472 (P-46), player/dungeon-chests #473 (P-47), player/ruin-walk #474 (P-48), world/material-library-3 #476 (W-166 r4), world/parts-units-3 #477 (W-175 r3b), b/fix-maxqueued-clamp #478, player/rain-shot #479 (P-50), fauna/dungeon-spawns #480 (#24 p2 r2; branch continues), world/ground-edits #481 (W-174 r2; branch continues), render/one-surface-3 #483 (R-23 r3), player/no-loading #484 (P-37 r1), player/dungeon-quests #485 (P-49), fauna/dungeon-spawns #486 (colossus families, chest loot rows; branch continues), folk/ownership #488 (A-134 r0-r3), world/ground-edits #489 (W-176; branch continues), player/cave-lantern #490 (P-37 r2), player/quest-items #491 (P-49 r3), folk/owner-steps-out #495 (A-134 r4), player/item-catalog #493 (P-35 r2; branch continues), player/quest-staff-hold #496, fauna/dungeon-spawns #497 (#24 p2 r3; branch continues), folk/guards-at-door #498 (A-134 r5), render/hour-sync-doc #500 (#494 note), folk/innkeeper-test-order #501, player/seamless-2 #502 (P-37 r3), world/ground-edits #503 (W-174 r3 + W-176; branch continues), world/map-edits-bake #504 (M-07 r2b), fauna/dungeon-r5 #505 (#24 p2 r4-r5), fauna/walkin-walls-doors #506 (P-37 r3 row 2), world/cave-walls #507 (W-172 r7), world/parts-units-4 #508 (W-175 r4-6), world/chunk-mesh-budget #509 (#400), player/item-ids #510 (P-35 r3 rows 2-5), world/ground-holes #512 (W-174 r4a), render/soft-wide-shadow #513 (R-40), player/tower-rooms #514 (P-51 r2), world/seams #515 (W-174 r4b-5), world/json-units #516 (W-175 world part), player/tower-roof #517 (P-51 r3), player/move-substep #519 (P-52), world/collars #520 (W-174 r6 row 1), world/fungi-caps #521 (W-172), player/tower-slip #524 (P-52 tail), fauna/ground-holes #525 (A-135), world/mouth-collars #526 (W-174 r6 row 2), world/cave-mushrooms #527 (W-177), world/mouth-holes #530 (W-180), world/cave-tendrils #531 (W-178 r1), world/fungi-fog #532 (W-179 r1), world/cave-setpieces #533 (W-178 r2), world/rock-walls #535 (W-162), water/truth #536 (W-153), world/uphill-graph #537 (W-153), world/river-notch #538 (W-153), world/river-revert #539 (W-153), world/river-profile-r1 #541 (W-153 rework r1-3), materials/cave-surface #542 (W-166 row 1), world/river-curtain #543 (W-153 row 3b), render/effects-kit #540 (R-29 electricity), materials/wood-kinds #544 (W-166 wood), materials/lib-tests #545 (W-166 tests), materials/coverings #546 (W-166 coverings), b/fix-cave-cut-flash #547 (#529 cut flash), materials/coat-switch #548 (W-166 coat switch), render/beach-glare #549 (beach glare), b/fix-pier-planks #551 (#550 planks), render/shore-foam #552 (R-42 shore foam), render/plume-reach #487 (#302 plume reach), world/cave-seam #553 (W-174 r3), world/river-lakes #554 (W-153 row 4), render/matched-shot #555 (R-43 shot helper), b/pier-stairs #556 (#550 stairs), b/pier-props #557 (#550 rope + fish), b/fix-stone-fallback #558 (#550 stone fallback), materials/cloth-metal #559 (W-166 phase A), materials/cloth-metal-b #560 (W-166 phase B), b/fix-copper-trim-fallback #561 (W-166 copper trim), world/fall-walls #562 (W-153 r3), world/cave-seam-2 #563 (W-174 r3 collars), materials/scarp-stone #564 (W-166 row 2), render/effects-kit-2 #565 (R-29 row 3), b/fix-mouth-floor #566 (#365 floor), materials/scarp-stone-2b #567 (W-166 row 2b), world/fall-sliver #568 (W-153 r3 lip slab + soft edges), materials/iron-kit #569 (W-166 iron).

Land later: player/p46 fb9bc62c (P-46 row 2, temple sockets out of walls + pin) is local only, not pushed, and conflicts with main. The fishing lane rebases and pushes it when resumed.

## Land: the owning lane merges main in, re-pins, verifies, hands off "ready for PR"
| Branch | Card | Owner | Note |
|---|---|---|---|
| render/cloud-e | R-35 r3 E #333 | render-2 | Built 25fbc044; waits on BENCH job 111 and shots. |
| world/river-profiles | W-153 #301 | world | WIP. |
| folk/a111-row1-parked | A-111 r1 | folk | Parked; only if A-110 slips. |
| world/log-bark | W-170 r1 | world | Clean; confirm verified. |
| world/ambient-details | W-142 | world | Job 69 FAIL at lava cave: fix seeps first. |
| world/cave-trickles | W-141 | world | Job 68 FAIL: cheaper version. |
| world/far-lite | W-132 | world | Check overlap with R-22/R-23. |
| world/cave-climbs | W-144 | world | Folded into W-162 row 3 (11-17): routes rebuilt on the shared grade source; hangings reuse W-178. Don't land. |
| render/cobble-depth | R-28 r2 | render | Terrain shader vs puddle-edges. |
| b/cloud-shadow-map | profiler | profiler | Rework: store raw density, apply R-20 r4 soft edge after the read; BENCH cost check. |
| render/foliage-realism | R-30 r1-r2c (#323) | render | Draft PR #457 (main merged in at 161ecc3a); waits on editor checks and job 102. |
| player/ships | P-44 r1 draft | player | Parked (Zach 11-16); input for the moving-bodies card. |
| render/far-island | R-22 r1 | render | Draft PR #482; verified, waits on BENCH job 107. |
| render/one-surface-5 | R-23 r4+r5 (#298) | render-2 | Draft PR #494; verified, waits on BENCH job 109 and the drift pair. |
| render/far-forest | R-22 r3 (#298) | render | Rebase after #482; PR after BENCH job 112. |
| render/grass-fields | R-31 (#325) | render | Prep at be206e54; waits on an editor slot. |
| render/cloud-picks, render/one-surface-4 | R-35, R-23 r4 | render-2 | cloud-picks waits on Zach's pick; one-surface-4 folded into one-surface-5 (#494). |
| render/effects-kit | R-29 (#320) | render-2 | Row 2 wired at 05a8043f, uncompiled; waits on an editor slot. |

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
