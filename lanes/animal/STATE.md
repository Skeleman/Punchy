# Animal lane: state (2026-11-02, after batch M-01)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin a077715, status clean apart from the old
untracked `Assets/_Recovery/` (from 10-30, not ours to delete). Editor open on port 7800, on animal-behaviours,
0 compile errors.

## M-01: both lanes merged into main
- main = 0cc3ed7 (PR #51, merge commit). Contains animal-behaviours a077715 and world-features 73709c8.
- Conflicts (docs only): CLAUDE.md (kept the animal docs/agents split; main's public note already in it),
  docs/agents/rendering.md (kept both bullets).
- Checks on the merge branch: 0 compile errors; v1 101 / E128CB1D, v2 127 / 831FF2EE, `[batfolk] picked 1` on v2.
- Builds/Windows is the M-01 build (merge/lanes-to-main 6cd3978).
- Branches left: remote `merge/lanes-to-main` (merged), local `merge/lanes-to-main`. Local `merge-test` ac8031a
  still there: `git branch -D` was blocked by the permission classifier (its merge commit is not on main; both
  parents are). Zach or the supervisor can delete it.
- Local `main` is stale (218d10d); fetch is current.

## Open
- #6: Zach's look at the eased leap (a25 shots). #12: Zach's look. #33: sideways still 0 m/s on stick right.
- #7, #16, #43, #49: Zach's look. #23: v2 confirmed (A-24), Zach can close.
- #41 unverified: host-to-client lunge not shown; plant shove never landed in a test.
- #10 new species (needs design). Animals still flee a flyer high above (confirm with Zach first).

## Test notes
- Shot list needs `-shotdir` (folder must exist); launch with `-quit`. `-spincheck` goes on the shot-list line.
- v1 and v2 hashes print in the player log as `[island] ... landmarks, hash` (no editor eval needed now).
- `-shotonhunt` can fire before the sprint is up to speed: check `hunter speed` in the huntside log.
- `-weather` forces at the player's spot: put `-goto` on an earlier line.
- `unity recompile` with no --port may hit the world editor; use an eval on --port 7800.
- BuildPlayer via `unity command --port 7800 eval "$(cat Temp/m01_build.cs)"` from Git Bash; times out at 30 s
  but finishes in a few minutes; wait for Temp/m01build.txt.
- Multi-line commit messages: use `git commit -F <file>` (PowerShell 5.1 splits a here-string `-m`).
