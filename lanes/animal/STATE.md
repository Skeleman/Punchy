# Animal lane: state (2026-11-02, after batch A-28)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin 0b06bf3 (A-28 on top of c601c66, which
merged main 0cc3ed7). Status clean apart from the old untracked `Assets/_Recovery/` (not ours). Editor on port
7800, 0 errors. Player log has `[island] generator version 1 (baked); 101 landmarks, hash E128CB1D`.
This branch still defaults to v1 with no flag (only `-island v2` switches); main/world's v2 default is not merged in.

## Folk talks (A-26 to A-28)
- CreatureBrainSystem: ChatFree is `internal static` (FolkTalkSystem uses it too). It allows Wander, Idle before
  MessageWait, Work on the way to or at the post (not Mine/Travel/Messenger/Critter, not fish or hermit once
  there), and Sit on the way to the seat. A talk from Work goes back to Work (ChatWalk 2, with the time left).
- Reach: ChatReach 30 m, they walk up to each other. Rarity limits unchanged: the dt/40 roll, the 90-200 s
  cooldown, MaxChats 3 within 60 m.
- Spacing (A-28, Chatting): want 1.25 m (child 1.9). Walking up they slow as the gap closes (Speed capped by
  the spare gap), stood they don't carry on. Closer than want - 0.15 (met at a well or seat): each steps back
  along the line between them, still facing, up to 0.9 m/s, only onto Walkable ground. Measured in the editor:
  four talks ended 1.28-1.29 m apart (the well pair at (133, -418) that stood 0.3-0.5 m in A-27 included).
- Each `FolkChat:` log line now ends with `N.NN m apart` (last seen while both were in the talk).
- Bubble (A-28): it was always drawn, but at Head + 0.1 (about eye height) as a bare "...", so it read as eyes.
  Now Head + 0.4 in a pale rounded 9-slice box (FolkTalkLabels.RoundBox). Still hidden by `-nohud`
  (InteractionPrompt.Hidden). FolkTalkSystem.Shown is only for lines said to the player (within 5 m of the
  player body), so a `-chatshot` camera never gets those; that is by design.
- `-chatmix` on the LAUNCH line logs a 15 s mix of nearby grown folk (see A-27).

## Open
- Build shot 1 (second `-chatshot` line, tall elder talking) has the bubble touching the top of his head;
  shot 0 is clean. If it matters, raise the bubble by StandHeight-relative height instead of a flat 0.4 m.
- Two-minute gaps between talks remain. Lowering ChatCoolMin or walking free folk toward each other changes
  the rarity limits: Zach's OK first.
- Folk talks are host-only in detail (Partner/ChatRole); clients see the Stage byte (gestures and bubble).

## Testing recipe
- Editor unattended Play: write launch args to `Temp/PlayArgs.txt` (e.g. `-chattest -shotlist <abs path>
  -shotdir <abs path>`), enter Play over the CLI, then read the console through UnityEditor.LogEntries reflection
  (Temp/a28_probe.cs also prints Bubbles/Shown counts and FolkTalkLabels state). Delete PlayArgs.txt afterwards.
- Build talk shot: launch `-chattest -quit`, list line `-place HumanVillage -time 10 -weather Clear -chatshot
  -wait 60` (HUD on). The chatshot fires about 4 s in at the Stonewick well pair.
