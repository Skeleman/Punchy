# Animal lane: state (2026-11-02, after batch A-04)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin b5e9715 (A-04: right face button, fish sprint).
Builds/Windows is the A-04 build. Editor closed; no Punchy.exe of the lane running.

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
- #33 wall climb (Lizard): not working. `-place wall` puts a wall; the stick pushes into it but OnWall stays False.
  No species has ClimbsAs AnySurface (Lizard.asset has no field; adding `ClimbsAs: 2` did not latch). Check
  PropSupport / KeepOutOfPropWalls `blocked` for that wall; the player stops at x 146.0 against a wall at 145.8.
- #11 Z-21 foot bend. #12 dormouse spins. #16 carrying large animals. #10 new species (needs design).
- Animals still flee a flyer high above (confirm with Zach before filing more).

## Test notes
- Shot list needs `-shotdir`; launch with `-quit`. Flags reset per line.
- New: `-actiontest roll:A,B|fly:Body|fish:Trout` logs rolls, descents and swim vs sprint; `-setbody X`; `-place wall`
  (with `-autowalk`) for the climb test; `-place clearlake` records the lake for the fish test.
- Climb test: `-autowalk -body Monkey -place tree:Oak`.
- Editor: `unity open .`, wait for `unity status` ready, `unity recompile`; BuildPlayer via eval using
  EditorBuildSettings.scenes (times out at 30 s but finishes: wait for a Temp file); `unity close <project path>`.
- Artefacts: PunchyMilestones/animal/a04/ (shots1-3, run logs, shots).
