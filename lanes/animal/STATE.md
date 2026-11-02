# Animal lane: state (2026-11-02, after batch A-19)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin e37a463 (A-19).
Builds/Windows is the A-19 build. Editor open on port 7800; no Punchy.exe of ours running.

## A-19 (artefacts PunchyMilestones/animal/a19/)
- #50 met: CreaturePersonality (Aggression) is worked out from the replicated Individual. Host and client logged the
  same values for all 10 dogs (`-personalitylog Dog`).
- #7 NOT met: dogs at aggression 0.6 and up hunt Rabbit and the hares, but no dog saw a live hare in the 40 s meadow
  run ("prey none", even though 8 Rabbits were spawned), so no chase and no Growl. Find out where the spawned
  rabbits go.
- #16 partly met: weights in kg in all 113 species, CarryCapacity 40 kg. Dog 36.4 kg was held; adult Human 65.2 kg
  was too heavy. HumanChild 23.6 kg passed the weight check, but the spawned (wild) child fled, so no pick-up.

## A-18 (artefacts PunchyMilestones/animal/a18/)
- #43: close calls (within 6 m) still play but spend the budget (debt down to 10). Herd test 73 calls in 2 min =
  36.5/min (A-15 ~47); after the first window 36.0/min. The extra call is the 2-call saved burst.
- #6: -huntside now counts shrubs/trees between camera and hunter and takes the clearer side. Meadow (-255, 27):
  whole wolf, 9.9 m/s, mid-bound with legs gathered, 0 plants in the way (hunt-800.png). Zach's look.
- #49: after the host quits, the client reloads into solo and logs DunesColossus and Mantrap placed again; 0 exceptions.

## A-16 (test-only, artefacts PunchyMilestones/animal/a16/)
- #12: player standing still, 16 Songbirds, 200 s: five 25 s+ Wander/flying windows, weighted ~22.5 deg/s.
- #7: Growl can't play with current data: Dog.asset has Hunts [], FleeDistance 0, Hunger 0, and no species hunts
  dogs, so Flee only comes from a Startle (Bolt). Needs a design call (dogs chase rabbits?).
- #48 met: client sees the host's Human upside down mid-somersault (client/remoteroll-800.png).

## A-15 (earlier): #33 Lizard mid-climb on barn face (open for Zach); #7 Hiss heard.

## Unverified (merged anyway; Zach's check in the build)
- #41 (A-10c, ae5b491): head glow fixed; host-to-client lunge not shown. Plant shove never landed in a test.
- #8 Z-52: live folk lines seen only twice.

## A-09 done
- #40 Dunes colossus: seed 1337 at (1780, 12.7, -1973), 67 m. Town distance not checked. GPU ms unavailable.
- #46 needs flat ground (a sand flat or lake shore) and one build to settle.

## Open
- #12: numbers in (A-16); Zach's look. #33: sideways still 0 m/s on stick right.
- #7: see A-19. #6, #43, #49: Zach's look.
- Dormouse Climb 11-70 deg/s, probably round a trunk; secondary.
- #10 new species (needs design).
- Animals still flee a flyer high above (confirm with Zach before filing more).

## Test notes
- Shot list needs `-shotdir` (the folder must exist, or shots are silently skipped); launch with `-quit` (add
  `-autowalk` only for climb tests; it scares songbirds). `-spincheck` goes on the shot-list line.
- `-spincheck` logs about every 30 s; songbird windows count all songbirds nearby: spawn 16 for 25 s+ windows.
- `-shotonhunt` brings the line's own shot forward (file is <line>.png). `-huntside 6` rides side-on 1 s into the
  sprint. `-weather` forces at the player's spot: put `-goto` on an earlier line.
- Shot lists survive the client's scene reload: a second line runs on in the solo game (A-18 #49).
- `-actiontest roll:A,B|fly:Body|fish:Trout`; `-setbody X`; `-place wall`; `-place clearlake`;
  climb test `-autowalk -body Monkey -place tree:Oak`. Client roll check: see TESTING.md.
- Editor: `unity recompile`; BuildPlayer via `unity command --port 7800 eval` (times out at 30 s but finishes, ~50 s).
