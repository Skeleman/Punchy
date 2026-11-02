# Animal lane: state (2026-11-02, after batch A-05)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin 609c5e5 (A-05: ankle cap, probes).
Builds/Windows is the A-05 build (without the #33 revert). Editor closed; no Punchy.exe of the lane running.

## Unverified (merged anyway; Zach's check in the build)
- #48 remote view of a roll (client sees the somersault from the Pose byte): never run on a client.
- #6 Z-50 gallop: never framed. Needs a hook (shoot when a hunter's Speed > 0.9 x RunSpeed).
- #7 Z-51: Hiss(49), Growl(52) not heard. Try Dog and Cat 4 m apart, or Dog next to Wolf.
- #8 Z-52: live folk lines seen only twice.
- #43: rate set by budget (0.6/s); no played-calls-per-minute count yet.

## Done this batch
- #48: B/Circle (the crouch action) rolls on the ground (0.53 s, 1.4-4.4 m by body), descends flying (Crow -9 to -12 m/s
  against -2.4 gliding), sinks swimming. Still cancels in menus.
- #47: fish sprint 2.8x speed (measured 4.1 -> 9.9 m/s) and holds depth (0.78 -> 0.38 m in a 1.8 m lake).

## Open
- #33 wall climb (Lizard): OnWall stays False; the player stops at the wall but never latches. A-05 ruled out ClimbsWalls
  and the prop head-height filter. Next: log `blocked`, pressing and the latch terms in PlayerMovementSystem.
- #12 dormouse/songbird spin: A-05 spincheck run (6 each, meadow, 66 s) logged nothing; DebugControls now logs
  "heading change" per species for Mode Wander every 30 s, but it printed none, so check Mode first.
- #11 done (cap 38 deg, AnkleBend in CreatureAnimator). #16 carrying large animals. #10 new species (needs design).
- Animals still flee a flyer high above (confirm with Zach before filing more).

## Test notes
- Shot list needs `-shotdir`; launch with `-quit`. Flags reset per line.
- New: `-actiontest roll:A,B|fly:Body|fish:Trout` logs rolls, descents and swim vs sprint; `-setbody X`; `-place wall`
  (with `-autowalk`) for the climb test; `-place clearlake` records the lake for the fish test.
- Climb test: `-autowalk -body Monkey -place tree:Oak`.
- Editor: `unity open .`, wait for `unity status` ready, `unity recompile`; BuildPlayer via eval using
  EditorBuildSettings.scenes (times out at 30 s but finishes: wait for a Temp file); `unity close <project path>`.
- Artefacts: PunchyMilestones/animal/a05/ (ankle-800.png, run1.log).
