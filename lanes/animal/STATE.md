# Animal lane: state (2026-11-02, after batch A-06)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin dcff671 (A-06: wall latch, probes).
Builds/Windows is the A-06 fix build, without the reverted FlyTo change. Editor open on port 7800; no Punchy.exe running.

## Unverified (merged anyway; Zach's check in the build)
- #48 remote view of a roll (client sees the somersault from the Pose byte): never run on a client.
- #6 Z-50 gallop: never framed. Needs a hook (shoot when a hunter's Speed > 0.9 x RunSpeed).
- #7 Z-51: Hiss(49), Growl(52) not heard. Try Dog and Cat 4 m apart, or Dog next to Wolf.
- #8 Z-52: live folk lines seen only twice.
- #43: rate set by budget (0.6/s); no played-calls-per-minute count yet.

## Done this batch
- #33 (partial): ClimbsWalls bodies count prop tops up to 8 m (WallClimbHead) as walls; on a wall the rim is looked for
  0.5 m ahead (WallLedgeReach); `-place wall` aims 1 m past the rim. Lizard: OnWall True, up at 1.3-1.4 m/s.
- Probes kept: WallProbe (SharedStatic, set by `-place wall` only) logs every latch term each frame;
  `-spincheck` logs heading change per species per Mode every 20 s.

## Open
- #33: at the top the Lizard stays OnWall instead of mantling; climbing down lets go and falls; sideways untested
  (the harness's sideways phase at 10-14 s comes after it has reached the top).
- #12: Songbird Wander turns 102-125 deg/s (other modes 0-3). Not the FlyTo touchdown orbit (holding heading within
  0.5 m and a 3 cm touchdown changed nothing). Next: split by Stage (Soar/FlyTo/Land), log YawRate and Speed for one bird.
- Dormouse Climb 11-70 deg/s, probably round a trunk; secondary.
- #16 carrying large animals. #10 new species (needs design).
- Animals still flee a flyer high above (confirm with Zach before filing more).

## Test notes
- Shot list needs `-shotdir`; launch with `-quit` and `-autowalk` (launch line only). `-spincheck` goes on the
  shot-list line. Flags reset per line.
- `-actiontest roll:A,B|fly:Body|fish:Trout`; `-setbody X`; `-place wall` (Lizard: climb takes about 3 s, so shoot
  at about 2 s for mid-climb); `-place clearlake`; climb test `-autowalk -body Monkey -place tree:Oak`.
- Editor: `unity recompile`; BuildPlayer via eval using EditorBuildSettings.scenes (times out at 30 s but finishes:
  wait for a Temp file).
- Artefacts: PunchyMilestones/animal/a06/ (probe3.log, fix.log, probe-wall/fix-wall/fix-spin.png).
