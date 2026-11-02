# Animal lane: state (2026-11-02, after batch A-26)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin 855a835, status clean apart from the old
untracked `Assets/_Recovery/` (from 10-30, not ours to delete). Editor open on port 7800, 0 compile errors.
animal-behaviours does NOT contain main's M-01 merge (main 0cc3ed7 is not an ancestor), so its player log has no
`[island] ... hash` line. Merge or rebase onto main before the next hash check.

## A-26
- #6 coyote: inherits Wolf's Leap (Coyote.asset is a Wolf variant); `-gaitlog` shows `gait Coyote: Leap`. No code.
- Folk talks: done in code, built, logged. Two `FolkChat: Human & Human ... in Stonewick for 10.x s` lines in the
  build run. Host/solo brain (CreatureBrainSystem "talking to each other"), local CreatureState fields
  (ChatCool, ChatRole, ChatWalk, ChatResume), Idle stages ChatTalk 70 / ChatListen 71 (no enum or ghost changes).
  Gestures in CreatureAnimator; "..." bubble and the log in FolkTalkSystem; `-chattest` / `-chatshot` in DebugControls.
- Task 3 shot NOT done: both talks ended before the first `-chatshot` line started (25 s); no talk at all in the
  next 120 s even with `-chattest`. Shots 1-2 show a lone folk.
- Suspect: once residents settle into their day jobs few are ChatFree (Wander or plain Idle, Leash > 0, no
  Patrol/Watch, no action). Unverified. Check the mix of Mode/Task/Leash among Stonewick folk at 30-60 s in
  play mode before changing ChatFree.

## Open
- A-26 retake: `-place HumanVillage -time 10 -chatshot -wait 40` (chatshot on the place line), then fix the
  eligibility if talks stay rare after settling.
- Synced talks would need the partner (and role) sent; now clients see only the stages (gestures, bubble).
- #6: Zach's look at the eased leap (a25 shots). #12: Zach's look. #33: sideways still 0 m/s on stick right.
- #7, #16, #43, #49: Zach's look. #23: v2 confirmed (A-24), Zach can close.
- #41 unverified: host-to-client lunge not shown; plant shove never landed in a test.
- #10 new species (needs design). Animals still flee a flyer high above (confirm with Zach first).
- Local `merge-test` ac8031a still there (delete was blocked); local `main` stale.

## Test notes
- Shot list needs `-shotdir` (folder must exist); launch with `-quit`. `-spincheck` goes on the shot-list line.
- v1 and v2 hashes print as `[island] ... landmarks, hash` only on branches with main's IslandV2 log.
- `-chatshot` frames only a joined, standing pair; talks come in a burst just after `-place`.
- `-shotonhunt` can fire before the sprint is up to speed: check `hunter speed` in the huntside log.
- `-weather` forces at the player's spot: put `-goto` on an earlier line.
- Editor CLI: `unity command --port 7800 eval "..."` (the port goes after `command`).
- BuildPlayer via `unity command --port 7800 eval "$(cat Temp/a26_build.cs)"` from Git Bash; wait for
  Temp/a26build.txt.
- Multi-line commit messages: use `git commit -F <file>` (PowerShell 5.1 splits a here-string `-m`).
