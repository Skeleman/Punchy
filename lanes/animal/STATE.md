# Animal lane: state (2026-11-02, after batch A-17)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin 17d4b40 (A-17).
Builds/Windows is the A-17 build. Editor open on port 7800; no Punchy.exe running.

## A-17 (artefacts PunchyMilestones/animal/a17/)
- #49 fixed: DunesColossus/Mantrap re-fetch TerrainSource on a world swap and rebuild on sceneLoaded. Host quits:
  client 0 exceptions. Colossus re-placement after the fallback not yet seen (list ends at the reload).
- #6: `-huntside 6` reshoot, clear: wolf at 7.1 m/s 1 s in, legs spread mid-bound, back half behind a shrub.

## A-16 (test-only, artefacts PunchyMilestones/animal/a16/)
- #12: player standing still (no -autowalk), 16 Songbirds, 200 s: Wander/flying windows 24.1 (38 s), 19.8 (30 s),
  10.5 (35 s), 16.0 (27 s), 46.4 (26 s), 28.5 (19 s). Five 25 s+ windows, weighted ~22.5 deg/s (A-14 ~23.5,
  A-15 ~12.7 from short windows). With 8 birds no window reached 25 s (max 24 s): birds mostly Rest/Idle/Graze.
- #7: Growl can't play with current data: Dog.asset has Hunts [] (never Hunt), FleeDistance 0, Hunger 0, and no
  species hunts dogs (not Hunted), so Flee only comes from a Startle (Bolt), which doesn't set Focus near.
  60 s of 3 dogs by 6 rabbits: Wander/Idle/Rest only, 0 voice lines. Needs a design call (dogs chase rabbits?).
- #48 met: client sees the host's Human upside down mid-somersault (client/remoteroll-800.png).

## A-15 (earlier): #33 Lizard mid-climb on barn face (open for Zach); #7 Hiss heard; #43 ~47 calls/min near a herd.

## Unverified (merged anyway; Zach's check in the build)
- #41 (A-10c, ae5b491): head glow fixed; host-to-client lunge not shown. Plant shove never landed in a test.
- #6 Z-50 gallop: still not seen (A-16 shot too early and too far).
- #8 Z-52: live folk lines seen only twice.
- #43: ~47 played calls/min near a herd (budget 36).

## A-09 done
- #40 Dunes colossus: seed 1337 at (1780, 12.7, -1973), 67 m. Town distance not checked. GPU ms unavailable.
- #46 needs flat ground (a sand flat or lake shore) and one build to settle.

## Open
- #12: numbers in (A-16); Zach's look. #33: sideways still 0 m/s on stick right.
- #7 Growl: design question above. #6: Zach's look; a spot without the shrub if he wants the whole body.
- Dormouse Climb 11-70 deg/s, probably round a trunk; secondary.
- #16 carrying large animals. #10 new species (needs design).
- Animals still flee a flyer high above (confirm with Zach before filing more).

## Test notes
- Shot list needs `-shotdir`; launch with `-quit` (add `-autowalk` only for climb tests; it scares songbirds).
  `-spincheck` goes on the shot-list line. Flags reset per line; a later `-wait` line stops a climb test.
- `-spincheck` logs about every 30 s; songbird windows count all songbirds nearby: spawn 16 for 25 s+ windows.
- `-shotonhunt` brings the line's own shot forward (file is <line>.png, not hunt0.png). Add `-huntside 6` for a
  side-on ride 1 s into the sprint. `-weather` forces at the player's spot: put `-goto` on an earlier line.
- `-actiontest roll:A,B|fly:Body|fish:Trout`; `-setbody X`; `-place wall`; `-place clearlake`;
  climb test `-autowalk -body Monkey -place tree:Oak`. Client roll check: see TESTING.md.
- Editor: `unity recompile`; BuildPlayer via `unity command --port 7800 eval` (times out at 30 s but finishes, ~50 s).
