# Unmerged branch triage (11-15)

None of these is on main (checked with merge-tree against origin/main). chemistry-*, rc/*, wip/*, tmp/*, test/* and active 11-14 branches are left out.

## Land: rebase, open a PR, card to the owning lane after RESUME
| Branch | Card | Size | Note |
|---|---|---|---|
| fauna/far-towns | A-109 | +929 | Job 72 PASS, merges clean. First. |
| world/cliff-kit | W-150 r1-2 | +1294 | Merges clean; 1c waited on #285 (merged). |
| world/no-water-discs | W-148 r5-6 | +849/-229 | Merges clean. |
| world/cave-entry | W-145 | +265 | Merges clean. |
| player/netsim | P-31 | +249 | Debug lag flag, merges clean. |
| render/audit-fixes | R-15 r1-3 | +141/-70 | Waited on R-18; merges clean. |
| render/swell-noise | R-17 | +296/-52 | Merges clean. |
| b/cloud-shadow-map | profiler | +141 | Saves 0.8-1.3 ms over forest; render reviews. |
| b/fix-swapchain-stall | profiler | +20 | Small fix; render reviews. |
| world/ambient-details | W-142 | +3309 | Job 69 FAIL at lava cave (+0.6 ms): fix lava seeps first. |
| world/cave-climbs | W-144 | +1163 | Conflicts; waited on Zach's look at the 1b pairs. |
| player/fishing | P-33 | +2161 | Conflicts; row 4 waits on Zach's feel notes. |
| world/far-lite | W-132 | +539/-80 | Conflicts; flight -6 ms GPU. Check overlap with R-22/R-23 first. |
| world/cave-trickles | W-141 | +1070 | Job 68 FAIL (+0.73 ms): needs a cheaper version. |

## Folded into other work
- player/clothing, hair, unify-items, items-pick, items-axe: inside take-items (P-38).
- player/armour, weapons: covered by take-items. player/tools: all but 32 lines; P-38 picks those up.
- player/seamless: P-37 row 3 builds on it.

## Superseded: archived and deleted 11-15 (Zach OK)
Each is kept as tag `archive/<branch>`. Owners check theirs on resume; restore with
`git push origin archive/<branch>:refs/heads/<branch>`.
light/forwardplus, b/r10-trims, b/r10-nocluster (R-10, redone in far-lights work); lod/screen-size (ScreenSize is on main);
render/cloud-shadow-soft, shadow-still (R-20 row 2 parked); b/r12-trims, b/r12-warp-bake (looked wrong);
b/water-rows-fixes, b/fix-waterfield-step, water/fine-grid (water trials); refactor/pose-by-size, fix/salt-ball-far-shadow;
world/mouth-rim, dark-crystal, cave-walk-46s, player/cave-water-camera (small; each owner confirms).

## Fix for the future
Every DONE row opens a PR (draft if it waits on something), and the supervisor sweeps unmerged branches daily.
