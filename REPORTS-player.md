# Player lane reports

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
