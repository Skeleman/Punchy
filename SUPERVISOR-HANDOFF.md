# Supervisor handoff, 2026-11-06 (to the player-lane session)

Zach asked the supervisor (main account) to hand off to you when its token budget ran out. You now also
supervise. Read COORDINATION.md first (rules 1-18; rule 18 = every process ends on its own, kill by PID).

## Lanes right now
- World (D:\Unity\Punchy-world, world-features): agent working W-110 row 2 (road tunnels), then W-115 rows 2-3,
  rest of W-110, W-117 (incl. GC row). Last pushed 32aa839. Reports in REPORTS-world.md.
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
