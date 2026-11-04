# Player lane reports

## 2026-11-03 batch P-4: 30 s net timeout fixes the timeouts; uncovers host refusing its own client
- DONE: NetDrivers.DisconnectTimeoutMS 10000 -> 30000 (player-client 18e305d, merged main incl. PR #93 first, no conflicts)
- Timeouts gone: 0 "disconnected (Timeout)" lines in 5 runs
- A (host v3, client v2): client "switching to v3", "sending world check B291FD17"; host "player 1 has the same world (B291FD17); welcome"; client "in game True, players 1", 48-62 animals, 60 fps. PASS for the joiner
- C (host v2, client -worldsalt 7): host "a player built a different world (0613F356, ours CAC3BAE8) ... Disconnecting them." PASS
- NOT DONE row 2 / host half of A: the HOST REFUSES ITS OWN LOCAL CLIENT in every run (host-only v2, A, C): host client "sending world check F5BF2604" (A; C 48648189) vs server B291FD17 / CAC3BAE8 -> "a player built a different world ... Disconnecting them", host ends "in game False, players 0". Was hidden before by the 10 s timeout dropping that client first
- Cause: WorldSync.Signature hashes palette.Landmarks names. FishingVillage (and turtle / sea folk village + landing) are in no scene (FishingVillage.asset guid not referenced by any .unity), so IslandV2System appends them to the palette at bake time, but only in the world that bakes. The host's client world takes the Shared bake path and never appends them -> different palette -> different fingerprint. Joiners bake themselves, so they match. On main since #36/#37/#38, not from P-2
- Proposed fix (IslandV2System, not lent this card): move the palette append (village, turtles, sea folk) into a helper and call it on the Shared path too, before the "shared with" log. ~10 lines, then rerun host-only + A + C
- Checks: compile 0 errors, DisconnectTimeoutMS 30000 read back in the editor; 1 build Succeeded 0 errors; 5 player runs all quit by -quitafter, no Punchy.exe left; editor closed; queue empty, RAM 14.3 GB at open
- Cost: ~17 calls, 1 build
- NEXT: lend IslandV2System for the palette fix above (one build), rerun the P-4 rows

## 2026-11-03 batch P-3: host self-timeout attributed (long bake vs 10 s timeout); -quitafter flag
- DONE: `-quitafter N` (Debugging/QuitAfter.cs, new file, LF): quits N s after start, real time (player-client 2d0b08b)
- FOUND: cause is the island bake blocking the main thread past NetDrivers.DisconnectTimeoutMS (10 s), not the version RPC:
  - mine 99cfd9c, host-only v2, x2: "Net (server): connection 1 disconnected (Timeout)", host "in game False, players 0" all run; bake 5988 ms (places 5425), a46b had 3154 ms (places 2607)
  - same build, host-only `-island v1` (no bake, my RPC still sent "sent island v1 to player 1"), x2: no timeout, "in game True, players 1"
- WORSE, partly mine: P-2 test A rerun (host v3, client v2, -gfx Low, -quitafter) the JOINER also timed out after "sending world check B291FD17" (P-2's run survived). Since P-2 the client bakes after it connects, so its whole bake counts against the server's 10 s. Don't merge P-2 alone
- NOT DONE: main 913d1b2 baseline build (needs a 2nd build; v1 runs attribute it instead). No fix: the fix is shared net code not lent to me
- Proposed fix (needs supervisor OK, NetDrivers.cs / GameBootstrap.cs): raise DisconnectTimeoutMS back toward 30 s at least for the in-process (IPC) link and while a joiner bakes; or bake off the main thread / split the places step across frames (world lane)
- Checks: compile 0 errors; 1 build Succeeded 0 errors; 7 player runs, all quit (QuitAfter "75.0 s, quitting" / "55.0 s, quitting"), no Punchy.exe left; editor closed; editor queue was empty, RAM 8.5 GB at open
- Noted only: DebugControls.ShotList NullReference after a lost connection (animal lane)
- Cost: ~30 calls, 1 build
- NEXT: supervisor decision on the timeout fix; then rerun P-2 A + C with -quitafter

## 2026-11-03 batch P-2: island version from host to client (v3 B14, brief 8B)
- DONE: host sends each new connection its baked island version (IslandVersionRpc); a joined client's IslandV2System waits for it (up to 15 s, then bakes its own with a warning) and bakes the host's version; solo/host keep the launch flag; bake cache keyed by version; fingerprint uses the version actually baked (check waits until baked); `-worldsalt N` test flag sets WorldSync.TestSalt (player-client 99cfd9c, on main 913d1b2)
- NOTE: new RPC type = network table change (RPC hash): builds before 99cfd9c can't join builds after it
- A host `-island v3`, client no flag: client "WorldSync: host runs island v3 (this launch asked for v2)" / "[island] host runs island v3; this launch asked for v2: switching to v3 (ClientWorld)" / "sending world check B291FD17"; host "player 1 has the same world (B291FD17); welcome". Client in game, 50 animals, 60 fps
- B host v2, client `-island v3`: "switching to v2", both CAC3BAE8, welcome
- C host v2, client `-worldsalt 7`: host "a player built a different world (0613F356, ours CAC3BAE8) ... host runs island v2. Disconnecting them."
- OPEN (not proven mine): in all 3 host logs the host's OWN local client timed out at start-up ("Net (server): connection 1 disconnected (Timeout)" right after the server bake) and the host stayed "in game False, players 0"; the joiner was fine. a46b (older main) host had no timeout, but start-up was slower here (island ready 10.1 s vs 7.5 s, scene stream latency 6.5 s vs 3.8 s; other instances were running). Next: one host-only run of a main 913d1b2 build vs this build to attribute it
- Also seen: C client NullReferenceException in DebugControls.ShotList (EntityQuery.TryGetSingleton) after "Lost the connection to the host"; animal lane's file, pre-existing path; shot list still finished and quit
- Checks: compile 0 errors (1 fix: WorldFlags instead of NetCode IsClient in Terrain code), 1 build Succeeded 0 errors, 6 player runs all quit, no Punchy.exe left; editor closed
- Shots: D:/PunchyMilestones/P-2/A-client.png (v2-launched client in game on the v3 island)
- Cost: ~58 calls, 1 build
- NEXT: attribute the host self-timeout (baseline run); a `-quitafter` flag so log-only net tests can run lighter (see RAM notes)

## 2026-11-03 batch P-1: resolution and window mode in the Graphics tab
- DONE: #78 Graphics tab (main menu, also Esc in game) has Resolution (slider over the monitor's sizes, deduped by WxH, 14 on this PC), Window (Full screen / Borderless / Windowed) and Apply display; saved in PlayerPrefs (Punchy.Display.*) and re-applied at start (player-client 1a9ef6c)
- DONE: keyboard / pad nav via MenuNav (slider steps left/right, buttons choose); no new tab, rows sit above Frame cap
- Checks: compile 0 errors; 2 builds (Succeeded, 0 errors; the 2nd fixed a bug found in run 2: a size not in Screen.resolutions, e.g. 1600x900, was saved raw but snapped at the next start, so 1600x900 came back as 1440x900 and the slider showed 640x480; now snapped before saving). 4 launches, 0 exceptions, all quit on their own, no Punchy.exe left
- Numbers: run3 `-display 1280x720,windowed` logged "Display: 1280 x 720 Windowed (from the command line, saved)"; run4 with no flag logged "Display: 1280 x 720 Windowed (saved, applied)", shot 1280x720 (previous saved state 1440x900, shot 1440x900)
- Shots: D:/PunchyMilestones/P-1/p1-run4-kept1280.png (relaunch kept 1280x720 windowed, menu shows Display applied); p1-run2-graphics.png (1440x900 before)
- Test flag: `-display WxH,full|borderless|windowed` applies AND saves (it stands in for the menu). Registry prefs were exported before the runs and restored after (Punchy.Display.* removed, Screenmanager values back)
- Not checked: Exclusive full screen / borderless on screen (kept test windows windowed so they didn't take over the display); editor Game view ignores the rows by design
- Cost: ~38 calls, 2 builds (1 over the card: the one-fix allowance)
- NEXT: Zach to try full screen and borderless by hand; then #85 camera or the next P card
