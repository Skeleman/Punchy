# Supervisor handoff, 2026-11-06 (to the player-lane session)

CANCELLED 11-06 late: Zach asked the main-account supervisor to continue. Player session: go back to the
player lane only; do not merge or review other lanes. This file stays as the handoff if the budget runs out.

Zach asked the supervisor (main account) to hand off to you when its token budget ran out. You now also
supervise. Read COORDINATION.md first (rules 1-18; rule 18 = every process ends on its own, kill by PID).

## Lanes right now
- World (D:\Unity\Punchy-world, world-features, head 11248778 pushed, main merged in): agent finished W-110
  (#39 road tunnels, complete), W-117 GC row and W-117 row 1 (water draw measurement). NOT reviewed or merged
  yet: review + PR. Next for world: W-117 rows 2-5 (plan in REPORTS-world.md 135a6bc1: merge far water chunks
  per 4x4 region, gate SSR by distance, flora LOD, shadow casters). Then burst-prune Punchy-world.
- Animal (D:\Unity\Punchy, animal-behaviours): agent working A-84 (Place spike, next colossi for #40, mire fog).
  A-83 merged as PR #177; #62 closed and logged in PLAYTEST.md.
- Player: you (P-20).
- Profiler (laptop): job 23 queued in BENCH-QUEUE.md (b/fix-proc-cleanup: timeouts, kill by PID). Review + merge.
Those two agents ran as subagents of the old supervisor session; if they stop, relaunch them from their cards.

## How the supervisor works
- Review each lane's pushed rows; merge with a PR when `git merge-tree --write-tree origin/main origin/<branch>`
  is clean (Zach granted gh pr merge; agents never merge or close issues).
- Close finished issues without asking; add a row to PLAYTEST.md (LF) for each.
- Keep lanes busy with cards in batches/; check open issues and lanes/ISSUES-MAP.md first.
- Editors: one per lane, open only with >= 6 GB free, queue in EDITOR-QUEUE.md; never kill another lane's editor.
- After an editor closes, prune old Burst cache: delete Library/BurstCache files older than 7 days in that
  project only (frees GBs; Unity never prunes it).
- No releases, ProjectSettings or machine-setting changes without Zach's OK. Public repo: no secrets/paths.
- Be brief with Zach; he is low on tokens.

## Open items for Zach (morning summary)
- Laptop display question (fps cap 60 stalls); one fpscap-60 check still to run on the main PC.
- Design: 4 or 6 attributes; hunger/thirst or not; overrides file per island or per area.
- #69 close?  Refactor PRs #171-176 status.  Cave creature count risk.
- PLAYTEST rows still owed: ribcage (W-114), GiantSpider in deep caves, cave dungeon sockets, A-82.
- Card pins changed after the ribcage: Fauna_Fingerprint 8A1979F1, Landmarks 111 3BDF705F.
- Untracked GameAudio.cs in the coordination tree: not the supervisor's; check whose.
