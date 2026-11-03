# Animal lane: state (2026-11-02, A-35 done, A-36 in progress)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin 4c03cde (A-35 gesture) on bf170f2 (merge of
main 41a9259, `--no-ff`). Lane hashes (player log, re-read after the merge): v2 126 / 053C63D5, v1 102 / 51EA6463.
Untracked `Assets/_Recovery/` is old and not ours. Editor on port 7800.

## A-35 results
- Slope seg 245: -goto -1448.0,83.2,238.7 -walk -1448.4,86.2,241.1. -nopushout walks up; push-out slides the body
  4 m back down (pushed 16.7 m in 2 s). A fixed -camrel loses the body after the slide: use
  `-camlook -1445.9,86.6,238.3,-1446.9,81.5,237.8` or the follow camera with `-view -10,20`.
- Merchant front-on: `-watch *:Merchant -watchfrom 1.6,2.5 -untilwork 60 -wait 8` (earlier waits catch it walking).
- CreatureAnimator Gesture: one hand raised ~1.9 rad for ~40% of a 3.8 s cycle, beckoning at 8 rad/s.

## Job work poses and props (A-33/34)
- CreatureAnimator Tend switches on FolkJobs.Of(s): Knead, TendBody, Haul, LookAbout + spear for guards.
- CreatureRenderSystem GearParts 8. All derived from job/task; Rule 11 holds.

## Shot flags (A-34)
- `-walk x,y,z`, `-untilwork S`, `-watch *:Job`, -place, -goto, -view, -camrel, -camlook, -watchfrom h,d,
  -nopushout, -time, -weather, -nohud, -chatshot, -wait, -shot. Launch: -shotlist, -shotdir, -quit, -island v1.
- Hashes: `[island] generator version N ... <n> landmarks, hash <X8>` line in the player log.

## Cave collision and camera (A-30/32)
- PushOutOfSolids runs only in caves, only on a moving player's step, gated by NoPushOut. It pushes a player
  back down a real 55 deg cave slope (seg 245): flagged for the supervisor.

## NEXT
- A-36 warp keys (DebugControls).
