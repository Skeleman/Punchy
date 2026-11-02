# Animal lane: state (2026-11-02, after batch A-13)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin dedc3db (A-13).
Builds/Windows is the A-12 build (`-mimicshot rest|run`, used with `-spawn OcotilloMimic:1 -place tree:Ocotillo`). Editor open on port 7800; no Punchy.exe running.

## A-13 done (animal-behaviours dedc3db, artefacts PunchyMilestones/animal/a13/)
- #33: `-place wall:<Species>` works (sets the body). Lizard on a 1.1 m wall (121.5, -489.4): UP at 1.1 s, TOP-OUT at 2.0 s (grounded on top, no stick), STEP-DOWN at 10.3 s, DOWN at 10.6 s; sideways 0.9 m/s. Events log as `wall test: UP/TOP-OUT/STEP-DOWN/DOWN`. Only a low wall found (not a tall garden wall). Mid-climb shot missed (wallclimb.png shows the village, no lizard): the side camera set by -place wall is overridden at shot time.
- #12: NOT met. Added a 100 deg/s^2 yaw-rate limit and a takeoff damp in FlyTo; SpinCheck counts Wander only after 5 s of it, windows 30 s. Songbird Wander/flying 0.1 (20 s sample), 28.3 (32 s), 56.7 (10 s) deg/s. Flying samples are short (10-32 s). Next: log per-bird turn with its target jumps; check Stage 1 re-targeting and the flare turn.
- #46: `-goflat` finds a dry spot with ground spread under 0.5 m over 30 m: (-357, -47), ground 21.6 m, spread 0.39 m. Fled of deer within 40 m, 4 samples, weighted vs flat (-flatfear): ground 1,0,0,0 vs 0,0,0,1; 5 m 0,0,0,0 vs 0,5,0,0; 15 m 0,0,0,0 vs 0,0,0,0; 30 m 0,0,0,0 vs 0,0,0,0. Ground row now matches (flat terrain); no gain from the weighting shows. Fear weighting looks neutral, not harmful.

## A-12 (earlier): see git history (mimic 2.0/3.2 done, colossus 968 m from a town).

## A-11b done (#17, open for Zach's look; animal-behaviours 8a3349e, Builds/Windows is this build)
- Mimic: 10 arms (5 mirrored pairs), splay 18-28 deg mean 23 (real recipe: 9-18 stems, 4-28 deg mean 16, 2.5-5 m). Mimic ~3 m.
- FleeDistance 6 -> 2.4: startle at 5.2 m (target 4-5; try 2.0 for 4). Fold 0.55 s unchanged. RunSpeed 5 -> 3: max 8.99 m/s (target 9-10; 3.2 = 9.6).
- Settle: Rest from curl 0 to 0.95 takes 4.97 s (curl eases exponentially at 0.6/s; the arms read up well before that).
- `-mimicshot rest|run|settle` (rest uses a fixed 3.5 m when the flora blob height reads 0). Real-vs-mimic numbers are logged at each start.
- Rest shot is far (camera 13 m back): both plants read, but small. Run shot: unclear which plant is the mimic.
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
