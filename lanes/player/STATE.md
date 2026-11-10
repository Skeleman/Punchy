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
- 11-09 (supervisor): NEXT = P-25 (door prompt, indoor camera, first person, seamless-interiors plan; Zach-approved).
  Skip cards marked DRAFT (P-23 items/gear, P-24 quests): they wait for Zach's review; the supervisor removes the
  DRAFT mark when they're approved. Then the lowest open card as usual.
- Queued after P-25 (Zach 11-09): P-26 talking to folk, then P-27 player stats. A-93 is MERGED (PR #238, main 18df736a):
  API FolkPersona.Of, FolkConversation.Topics/Ask/Respond, lines in dialogue.json `conversation`/`beastTalk`. Respond and the
  favour cooldown are host/solo-local: a joined client needs an RPC to the host (part of P-26).
- P-22 done and merged 11-09 (PR #229, main): roof undersides, small-body cave camera (#85), display modes (#78).
- P-21 merged (lights/round3). Older history: git log of this file.
- RAM: 3 other editors are open tonight (R-7, R-8, W-129). Open yours only with >= 6 GB free (start-quiet.ps1);
  if short, wait for one to close (check every 10 min, stop after 3 h).
