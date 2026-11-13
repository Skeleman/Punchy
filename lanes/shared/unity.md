# Unity editor, builds and processes on the main PC (rules 14, 15, 17, 18)

## Before any Unity start
1. `D:/Unity/PunchyBench/HOLD` exists: a bench window is on. Start nothing; let what runs reach a save point and
   close it; do work that needs no Unity. Never create or delete the file.
2. Free RAM under 6 GB: tell the supervisor instead of waiting.
3. Start every editor and build window through `tools/start-quiet.ps1` (coordination tree), in the background:
   `powershell -ExecutionPolicy Bypass -File <coordination>/tools/start-quiet.ps1 -Exe "<exe>" -ArgLine "<args>"`.
   Set the Game view to Play Unfocused before the first play-mode test. `-batchmode` runs don't need this.

## Editor (rule 15)
- One editor per worktree, opened only by its lane. Target it by PID and project path, never by port alone.
- Keep it open across the rows of a card. Close it (your own PID) when the suite needs it closed, before switching
  commits, when you hand the card back, or before you stop.
- Drive it with `unity command eval` / `unity command script`. For long work write the result to
  `Temp/<name>.txt` and wait for the file; the CLI returns after 30 s but the work goes on.
- `EditorApplication.delayCall` never fires in an idle editor: call the work directly.
- Editor killed: delete that tree's `Temp/__Backupscenes` before reopening.
- A merge that changes a scene can raise a "reload the scene?" dialog that blocks the editor and the CLI.
- No Unity API calls in static initialisers of types Burst reads (`DetailPolicy`, tuning classes): the Burst
  compile fails silently and jobs run managed (shots take 60-100 s, not 5). Crawling shots: check the console first.
- Install editors only with `unity install <ver> --no-elevate -d D:\Unity\Editors\<ver>`.

## Builds and players (rule 14)
- Build only to your tree's fixed path (`Builds/Dev/Punchy.exe`) and run from there: a new exe path raises a
  firewall prompt that stalls the run. Never touch firewall prompts or rules. If the path is locked by a player
  that won't exit, build to `Builds/DevB` and say so.
- Never pass `-screen-*` flags (they become Zach's saved preference) or `-nographics` (compute buffers break).
- From Git Bash, pass player paths with forward slashes.
- Never send keystrokes or grab windows outside the Punchy window; use in-game shot lists.

## Every process ends on its own (rule 18)
On Windows, stopping a Monitor, background task or agent kills only its top shell; children (`tail -f`, greps,
loops, Punchy.exe) live on.
- Wrap every Monitor or background command in `timeout <s>`; give every loop an end time
  (`end=$((SECONDS+1700)); while [ $SECONDS -lt $end ]`). One watcher at a time.
- Launch every Unity or Punchy run with `timeout`. End only processes you started, by PID, never by name: other
  lanes share the machine.
- Bound every pipe: `curl --max-time 120 ... | head -c 1000000 | grep -o ...`, or write to a file and read it.
- Before you report: no Punchy.exe of yours in `tasklist`, and the bash and grep counts have not grown.
- A Punchy.exe with no path, 0 threads and 0 MB is a dead entry, not another lane's game.
