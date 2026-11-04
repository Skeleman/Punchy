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
5. Take the lowest open card `batches/P-*.md` not marked done in REPORTS-player.md. When a card is done, take the next open one straight away (the supervisor keeps two queued). None open: stay idle, don't poll.

## Rules
Never merge PRs, close or comment-close issues (one-line progress comments are fine). Keep line endings per file
(`git ls-files --eol` before add). Leave Assets/Temp, Assets/_Recovery, QualitySettings out of commits. Public repo:
no machine paths or personal info. Never bare `git stash`.

## In flight
- Queue 11-04: P-9 (temple chests, small-body camera, sky), P-10 (doors and interiors, #25), P-11 (items and inventory, #26).
- Queue 11-04 (4): P-16 (quest UI fixes, interior light, ruin/cave camera check, pad inventory), P-17 (host-checked hand-ins, clear-an-area quests). PR #128 merged (21aa5ab).
- Queue 11-04 (3): P-14 (#26 object inventories), P-15 (#27 quests first slice). P-13 merged (PR #119, da2e0c0).
- Queue 11-04 (2): P-12 (chest model with a lid, gamepad inventory, host/solo log nit; #99 fallback first if q23 still crashes on the laptop).
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
- P-10 done 11-04 (717e007): Use key F/RB + Keys tab, host-owned doors. Interiors blocked (world-lane collision hook, see report). P-11 next.
- P-11 done 11-04 (6621249): items, host pickup, inventory, seeded temple chests. Nothing in flight.
- P-10 0a 11-04 (38a8f4c): #99 teardown reordered (jobs first); clean on desktop in both orders, needs the laptop to confirm.
- P-12 done 11-04 (05baa8a): chests with lids, MenuNav inventory (pad unverified), solo/host labels. #99 still waits on the laptop's q23 on 38a8f4c.
- P-12 row 0 11-04 (46abd95): #99 two-frame gap; desktop release 15/15 clean; waiting on the laptop's q23.
- P-13 done 11-04 (f4e050a): house interiors (7 seeded rooms, 98 doors lead inside), door teleport, host-owned lamps.
- P-14 done 11-04 (8413361): containers (chests, room storage), host-owned; drop cap 256. P-15 next.
- P-15 done 11-04 (77887d3): quests archive + notice boards + host-owned progress. Nothing in flight.
