# Animal lane: state (2026-11-03, after batch A-08)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin 32f90e4 (A-07: height fear, call switches, wall top release).
Builds/Windows is the A-07 build (has `-flatfear -nobiometable -callrate -calllog`). Editor open on port 7800; no Punchy.exe running.

## Unverified (merged anyway; Zach's check in the build)
- #48 remote view of a roll (client sees the somersault from the Pose byte): never run on a client.
- #6 Z-50 gallop: never framed. Needs a hook (shoot when a hunter's Speed > 0.9 x RunSpeed).
- #7 Z-51: Hiss(49), Growl(52) not heard. Try Dog and Cat 4 m apart, or Dog next to Wolf.
- #8 Z-52: live folk lines seen only twice.
- #43: rate set by budget (0.6/s); no played-calls-per-minute count yet.

## A-08 done
- #9: hunt code was already on animal-behaviours (wip/animal-p3 is an ancestor). -huntlog 190 s: 12 stalks, 4 pounces (2 landed), 2 ate, 7 tired. No mid-chase shot yet (needs a hook that shoots when a hunter's Stage is Running).
- #46 deer-only (3 runs, Fled/~7): 30 m after 0,0,0 vs flat 2,2,1; 5 and 15 m noise-sized; ground differs 0 vs 4-6 though formulas match (unexplained). Artefacts: PunchyMilestones/animal/a08/.
- Builds/Windows is the A-08 build (-lift counts deer only). Pushed animal-behaviours.

## Done earlier (A-06b)
- #12 (mostly): FlyTo flare eases turning off and drifts onto the spot. Songbird Wander 100-125 -> 25-31 deg/s;
  flare stage 268-278 -> 18-25. `-noflaresettle` gives the old way from the same build.
- `-spincheck` splits Wander into /ground /flying /flare /stage2+ and traces one songbird (40 lines, 0.5 s).
- Wall test sideways phase moved to 1.5-3.5 s after `-place wall`.

## A-07 done
- #46 height fear x2.5 past 1.5 m (log inconclusive: counts all creatures; filter to deer). #43 Meadow 36-60 calls/min (was 96-132), Dunes 0, Glacier 0.
- #33 flat boulder: topped out, OnWall False, sideways 0.9 m/s. Not yet on a garden wall.

## Open
- #12: left over is Wander/flying 28-35 deg/s (was 30-34 before too) and /ground 31-46 (7-21 s only). Under 20
  needs the flying stage looked at: trace is used up by the first bird; re-pick it per run or per stage.
- #33: `-place wall` now picks a round hut in a settlement (rim 30.1, conical thatch roof). Lizard climbs at
  0.5-1.2 m/s, ends 1.2 m in over the eave at feet 30.8, still OnWall, stick 0 (harness stops 0.3 m from goal),
  so the top-out (needs pressing) never runs. Sideways: stick right gives along 0.0 m/s. Down: on stick down it
  lands on the roof (OnWall False), walks off and falls at -3.6 m/s; the step-off latch needs wasOnProp, which a
  roof/terrain top may not set. Next: release OnWall when support is under the feet without stick; drop the
  wasOnProp guard (use "had ground under the feet and now a drop > stepUp"); log why sideways is 0
  (WallProbe line while stick right). Better: choose a flat-topped wall for `-place wall`.
- Dormouse Climb 11-70 deg/s, probably round a trunk; secondary.
- #16 carrying large animals. #10 new species (needs design).
- Animals still flee a flyer high above (confirm with Zach before filing more).

## Test notes
- Shot list needs `-shotdir`; launch with `-quit` and `-autowalk` (launch line only). `-spincheck` goes on the
  shot-list line. Flags reset per line, and every new line clears the climb test: a later `-wait` line stops it.
  For a 2 s shot plus a long run, place twice (two lines each with `-place wall`).
- `-spincheck` logs every 20 s on a clock that runs across lines: windows straddle lines; use a list of one spin line.
- `-actiontest roll:A,B|fly:Body|fish:Trout`; `-setbody X`; `-place wall`; `-place clearlake`;
  climb test `-autowalk -body Monkey -place tree:Oak`.
- Editor: `unity recompile`; BuildPlayer via eval using EditorBuildSettings.scenes (times out at 30 s but finishes:
  wait for a Temp file, about 65 s).
- Artefacts: PunchyMilestones/animal/a06b/ (fix.log = before/after spin, fix2.log = clean after + full wall run).
