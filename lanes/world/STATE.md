# World lane: state (2026-11-02)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.

## Branches in flight (pushed, not merged into world-features)
- `world-laptop/z34-pool` 91f6b6a: #3 Z-34, a cave stream gets a pool or ends at a hole/crevice (Zach's rule:
  no pool only where it drops into a hole or crevice). Compiled, not checked in game.
- `wip/ui-menu` e0c7814: UI-1 main menu (0796b07), icons from Kenney Input Prompts 1.5A (09a79ee), #4 "Exit game"
  button with confirm. Compiles, not built.
- `wip/ui-wheel` 0f05394 (6feebef merges ui-menu in): #5 controller text-entry wheel for menu fields. Compiles,
  not built.
- `b/bench-runner` db3c1f2: `-benchday` (any weather at any stop). Tooling only.

## Needs a slot (build + one shot session)
- #4 + #5: `-menu Graphics -forcepad xbox` (foot of the menu, the confirm; A then B cancels) and
  `-menu Multiplayer -forcepad xbox` (Code and IP wheels: enter, delete, ring switch, cancel).
- #3: frame the stream end at (-1454, 37, -1153) with `-place cavestream` before and after.

## Open, not started
- #15 look checks owed: Z-30 hall/inn posts close up (Pen not on this seed), Z-10 fog over a non-marsh river.
- #13 Z-35 fall speed: Zach judges in play. #14 cave-mouth shards: not reproduced; needs a spot from Zach.
- P3 rows needing a design note first: #17 Z-54, #18-#22 Z-60..Z-64. Roadmap: #23 realms, #24 dungeons.
