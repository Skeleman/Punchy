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
- Queue 11-04: P-9 (temple chests, small-body camera, sky), P-10 (doors and interiors, #25), P-11 (items and inventory, #26).
- P-1 done 11-03 (player-client 1a9ef6c): resolution + window mode in the Graphics tab (#78). PR #90 merged 11-03 (5a56458).
- P-2 done 11-03 (player-client 99cfd9c), but don't merge alone: joiners can time out while baking (P-3).
- P-3 done 11-03: -quitafter flag; host/joiner timeouts = bake blocking main thread past the 10 s DisconnectTimeoutMS. Waiting on a supervisor call on the shared net-code fix.
- P-4 done 11-03 (player-client 18e305d): 30 s timeout; timeouts gone, joiners pass. Host refuses its OWN client (palette landmark types appended only by the baking world; fix needs IslandV2System, see report).
- P-5 done 11-03 (player-client 0b2309d): camera underground (#85). Caves verified; temple room not shot yet (-place temple finds no room); small-body pivot open.
- P-6 11-03: DungeonSocket contract d7ed5eb (posted). Set pieces blocked: underground collision is world-lane code; options in the report.
- P-7 (11-04): temple fix, small-body camera pivot, temple dungeon sockets + debug draw (supervisor picked the temple-rooms option). P-8 after: sound and look settings.
- P-7 done 11-04 (player-client be6d005): -place temple fixed, small-body camera (partial), temple socket buffer (68, hash 9C11B352). P-8 next.
- P-8 done 11-04: #99 host-twice crash fixed (84208ff, 5 netcycle runs clean); Sound/Look tabs + -settings (f332f30). Nothing in flight.
- P-9 done 11-04 (a4994d1): chests 13 (hash 0EBAB9D3), small body in frame, temple roof camera fix. Nothing in flight.
