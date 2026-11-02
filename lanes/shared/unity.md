# Unity editor and builds on the main PC

- One editor per worktree, and only the lane that owns the worktree opens it. The profiler benches on the laptop.
- Drive the editor through `unity command eval` / `unity command script`. For anything long (builds), write the
  result to `Temp/<name>.txt` and wait for that file; the CLI returns after 30 s but the work carries on.
- `EditorApplication.delayCall` never fires in an idle editor: call the work directly.
- Close the editor before switching commits (a "modified externally" dialog blocks the CLI). If the editor was
  killed, delete that tree's `Temp/__Backupscenes` before reopening.
- Build players only to the tree's fixed path (`Builds/Dev/Punchy.exe` in the world tree, `Builds/Windows/` in
  the animal tree) and launch them from there. A new exe path raises a Windows firewall prompt that stalls runs.
- Don't pass `-screen-*` flags to test players (they become Zach's saved preference), or delete the
  non-"Default" `Screenmanager*` values afterwards.
- Never send keystrokes or window grabs outside the Punchy window; use in-game shot lists.
- Install editors only with `unity install <ver> --no-elevate -d D:\Unity\Editors\<ver>` (no UAC prompts).

## Players must exit on their own
- A player left running locks `Builds/.../Punchy.exe`, and the next build fails (W-02 and A-03 were both blocked this way).
  Agents are not allowed to kill processes. Make the last shot-list line quit the player, or launch it so it exits
  when the list ends, and check `tasklist` shows no Punchy.exe of yours before you report.

## Player launch gotchas (W-04)
- Never pass `-nographics` to Punchy.exe: compute buffers break and the shot list never finishes, so it never quits.
- From Git Bash, pass player paths with forward slashes (`D:/PunchyScratch/...`); backslashes get eaten.
- If Builds/Dev is locked by a player that won't exit, build to Builds/DevB instead and say so in the report. Don't kill it.
