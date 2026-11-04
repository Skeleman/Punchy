# Player lane: state (new 2026-11-03)

Branch player-client, worktree D:/Unity/Punchy-player, based on main 27bf8c1 (first editor open imports a fresh Library).
Run by a second account on the main PC. The supervisor (main account) writes cards in `batches/P-*.md`, reviews and merges.

## Scope
Now: settings and menus, the camera, client/host sync, debug and teleport shortcuts, test tools.
Later (Zach 11-03): buildings you use: interior cells, doors and lights you interact with, picking up objects,
inventory (#26). Kit shapes and collision stay with the world lane until handed over.

## First session on this account
1. Read this file, `FILES.md`, `lanes/README.md`, `lanes/shared/unity.md`, `git.md`, `report.md`.
2. `gh auth status`; `git pull --rebase` here; check `git -C D:/Unity/Punchy-player status -sb`.
3. Work only in D:/Unity/Punchy-player. Your editor: `--project-path D:\Unity\Punchy-player`. Never send to the
   D:/Unity/Punchy or D:/Unity/Punchy-world editors; never kill their Punchy.exe or editors.
4. Build only to `Builds/Dev` inside your own tree. RAM is tight: one build at a time, quit your player runs.
5. Take the lowest open card `batches/P-*.md` not marked done in REPORTS-player.md. None open: stay idle, don't poll.

## Rules
Never merge PRs, close or comment-close issues (one-line progress comments are fine). Keep line endings per file
(`git ls-files --eol` before add). Leave Assets/Temp, Assets/_Recovery, QualitySettings out of commits. Public repo:
no machine paths or personal info. Never bare `git stash`.

## In flight
- P-1 done 11-03 (player-client 1a9ef6c): resolution + window mode in the Graphics tab (#78). Nothing in flight.
