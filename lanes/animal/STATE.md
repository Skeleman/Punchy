# Animal lane: state (2026-11-02, after batch A-14)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin 753e5de (A-14).
Builds/Windows is the A-14 build. Editor open on port 7800; no Punchy.exe running.

## A-14 (animal-behaviours 753e5de, artefacts PunchyMilestones/animal/a14/)
- #33: `-place wall` now wants a flat-topped wall of 2 m or more and walks through settlements (logs `wall search:`
  with the tallest flat top it saw, gives up after 8). Found a 4.9 m barn wall at settlement 6 (519, -937).
  Player: UP 1.0 s, TOP-OUT 6.6 s (4.9 m up), STEP-DOWN 10.2 s, DOWN 10.8 s. The shot list waits for the
  placement now, but the mid-climb shot (wallclimb.png, 1.2 s) has the camera hard against the barn: the wall face
  is faded out and no lizard shows. NOT met. Next: frame from the `to watch` camlook (8-10 m back) at shot time, or
  pull the side camera back past the building's fade distance.
- #12: per-bird flight trace on `-spincheck` (retargets, takeoff and flare turns with yaw rate). Cause named: two
  parts. (1) Takeoff turns to spots behind the bird (bearing 100-180 deg, 150-280 deg/s in the first 3 s); some are
  tree picks whose search circle reaches behind. (2) Wheeling near the spot (140-800 deg within 6 m) when the bird
  arrives high or at an angle and circles back at minimum airspeed. Fix kept: trees only within 75 deg ahead and
  8 m off. Player run Wander/flying: 27.2 (40 s), 28.4 (18 s), 15.3 (16 s), 5.8 (6 s) deg/s. NOT met.
  Tried and reverted: a turning-circle speed cap (never bit), a land-ahead retarget (61-68 deg/s, worse).
  Next: trace why ground-spot bearings exceed 90 deg at takeoff; let it descend steeper near the spot instead of
  wheeling.

## A-13 (earlier): #33 1.1 m wall UP/TOP-OUT/STEP-DOWN; #46 fear weighting neutral on flat ground.

## A-11b done (#17, open for Zach's look; animal-behaviours 8a3349e, Builds/Windows is this build)
- Mimic: 10 arms (5 mirrored pairs), splay 18-28 deg mean 23 (real recipe: 9-18 stems, 4-28 deg mean 16, 2.5-5 m). Mimic ~3 m.
- FleeDistance 6 -> 2.4: startle at 5.2 m (target 4-5; try 2.0 for 4). Fold 0.55 s unchanged. RunSpeed 5 -> 3: max 8.99 m/s (target 9-10; 3.2 = 9.6).
- `-mimicshot rest|run|settle` (rest uses a fixed 3.5 m when the flora blob height reads 0). Real-vs-mimic numbers are logged at each start.
- Spawn rule unchanged (FaunaDirector.TrySpawn Sonoran 0.2/ha). Artefacts: PunchyMilestones/animal/a11b/.

## Unverified (merged anyway; Zach's check in the build)
- #41 (A-10c, ae5b491): head glow fixed (tooth-tip zero normals 1008/864 -> 0; now normalizesafe). Hit check 7/7 at 0.8 m (A-10b). Shots a10c show the head open, no glow. Host-to-client lunge not shown (remote player not a target).
- #41 plant: player shove never landed in the test (autowalk out of reach); client view not run.
- #48 remote view of a roll (client sees the somersault from the Pose byte): never run on a client.
- #6 Z-50 gallop: `-shotonhunt` now frames a sprint; check hunt0.png shows a gallop pose.
- #7 Z-51: Hiss(49), Growl(52) not heard. Try Dog and Cat 4 m apart, or Dog next to Wolf.
- #8 Z-52: live folk lines seen only twice.
- #43: rate set by budget (0.6/s); no played-calls-per-minute count yet.

## A-09 done
- #40 Dunes colossus (Fauna/DunesColossus.cs, Shaders/Colossus.shader, Resources/Colossus.mat): seed 1337 at (1780, 12.7, -1973), 67 m, biggest Dunes region 93 cells. Head turn 37 s, arm drag 50 s with furrow, sandstorm. Town distance NOT checked. GPU ms unavailable (FrameTimingManager gives 0; frames vsync-capped 16.7 ms both ways): enable frame timing stats to measure.
- #9 `-shotonhunt`: wolf sprint at turkey, 14 m apart, shot saved (hunt0.png). Prey can be any species.
- #46 ground row: code identical below 1.5 m gap; spawned herd sits on slopes (gap mean 0.2-6 m, up to 6 over 1.5 m), so weighted vs flat differ by terrain, not code. Fled sample 1: weighted 0,5,4 vs flat 3,0,0 (no pattern). Needs flat ground (a sand flat or lake shore) and one build to settle.
- Artefacts: PunchyMilestones/animal/a09/ (player.log, list.txt, col300/col60 plus -800).

## Open
- #12: Wander/flying 15-28 deg/s; takeoff turn and near-spot wheeling (see A-14).
- #33: mid-climb shot framing (see A-14). Sideways still 0 m/s on stick right.
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
