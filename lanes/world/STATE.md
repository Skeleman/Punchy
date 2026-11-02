# World lane: state (2026-11-02, after W-01)

Worktree `Punchy-world` (main PC), branch `world-features`. Backlog: `gh issue list -l lane:world`.
Released: v0.11.0 = world b39522b + animal c810406.
Branch head: a96a1dc (pushed). It holds: wip/ui-wheel (UI-1 menu, #4 Exit game, #5 pad wheel; CLAUDE.md kept as the
docs index), z34-pool (#3), #34 blue moon, #32 roof lips. Built to Builds/Dev, no exceptions in the player logs.

## Branches still unmerged
- `b/bench-runner` db3c1f2: `-benchday`. Tooling only.
- wip/ui-menu, wip/ui-wheel, world-laptop/z34-pool are now inside world-features; safe to delete later.

## Checked in W-01 (see REPORTS-world.md)
- #3: stream 2 end moved (-1453.9,36.7,-1152.7) to (-1446.9,39.2,-1152.0), 15 to 10 points, ends in a pool; stream 0
  also shortened (15 to 10). Streams 1, 3, 4 unchanged.
- #34: radius 0.034 to 0.13 rad, bluer, one thin crack, maria and small craters (Zach's latest wording applied).
- #32: lip ring under the rim of Hut, StoneHouse, Tower, Shrine roofs (about 0.14 m). Subtle at town distance.

## Still unchecked (needs hands or a pad)
- #4/#5: pad input (A then B on Exit confirm; Code/IP wheel enter, delete, ring switch, cancel). A shot list can't
  press buttons; `-menu Multiplayer -forcepad xbox` shows glyphs and the Exit game button at the foot. The
  `-menu Graphics` shot came out with no menu open.
- Landmark hash: no landmark or placement code touched, but not measured; z34's CaveBaking change alters cave
  streams, so run the WorldProbe hash once.

## Open, not started
- #15 look checks owed: Z-30 hall/inn posts close up, Z-10 fog over a non-marsh river.
- #13 Z-35 fall speed: Zach judges in play. #14 cave-mouth shards: needs a spot from Zach.
- P3 rows needing a design note first: #17 Z-54, #18-#22 Z-60..Z-64. Roadmap: #23 realms, #24 dungeons.
