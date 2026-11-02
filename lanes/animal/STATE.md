# Animal lane: state (2026-11-02, after batch A-27)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin c601c66 (merge ed767e0 brought in main
0cc3ed7). Status clean apart from the old untracked `Assets/_Recovery/` (not ours). Editor on port 7800, 0 errors.
The player log now has the `[island] generator version 1 (baked); 101 landmarks, hash E128CB1D` line.

## Folk talks (A-26 / A-27)
- CreatureBrainSystem: ChatFree is now `internal static` (FolkTalkSystem uses it too). It allows Wander,
  Idle before MessageWait, Work on the way to or at the post (not Mine/Travel/Messenger/Critter, and not fish
  or hermit once there), and Sit on the way to the seat. A talk from Work goes back to Work (ChatWalk 2, with the
  work time left).
- Reach: a grown one can stop another up to ChatReach 30 m away, and they walk up to each other. FindChat
  searches ceil(reach/16) cells around. The rarity limits are unchanged: the dt/40 roll, the 90-200 s cooldown,
  MaxChats 3 within 60 m.
- `-chatmix` on the LAUNCH line (it reads LaunchArgs) logs every 15 s: grown folk within 80 m of the camera,
  leashed, cooling, free, starters, stale roles, the closest free pair, talks of 4 s or more, and modes.
- Measured: no stale ChatRoles. What limits talks now: each folk who has talked cools for 90-200 s, and the free
  ones left are often 30-130 m apart. Build runs: 3, 1 talks per minute (114 s run); 1, 1, 0, 0, 1 (276 s run).
  So there are still 2-minute gaps.

## Open
- Talk shot not done (A-27 task 3). `-chatshot` caught the same pair at the Stonewick well (127, -418) both
  runs. They stood 0.3-0.5 m apart (overlapping, not the ~1.2 m talk spacing), and no bubble showed.
  - `-nohud` hides bubbles: it sets InteractionPrompt.Hidden, which FolkTalkSystem.OnGUI checks. Shoot talks
    without `-nohud`, or make bubbles ignore it.
  - The bubble was also missing with the HUD on. Check when FolkTalkSystem.Shown gets an entry.
  - Check the spacing: the walk-up target when both are already near (well/seat).
- If gaps of 2 minutes or more are unwanted: lower ChatCoolMin, or let a free folk walk toward the nearest
  free one. Both change the rarity limits, so get Zach's OK first.

## Testing recipe
- Editor unattended Play: write launch args to `Temp/PlayArgs.txt` (e.g. `-chatmix -quit -shotlist <abs path>
  -shotdir <abs path>`), enter Play over the CLI, then read the console through UnityEditor.LogEntries reflection
  (the default Editor.log is stale). Delete PlayArgs.txt afterwards.
