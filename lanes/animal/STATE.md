# Animal lane: state (2026-11-02, after batch A-20)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin e2eda9a (A-20).
Builds/Windows is the A-20 build. Editor open on port 7800; no Punchy.exe of ours running.

## A-20 (artefacts PunchyMilestones/animal/a20/)
- #7 met in the build: dogs had no prey because the Resources Dog's Hunts pointed at Resources copies of the hares
  (reference match). FaunaDirector.Lists now matches by name. Dog 0.60 stalked a hare and Growl (52) played at 7 m;
  a dog at 0.68 ate a rabbit. Calm dogs (0.49 at 4.7 m, 0.31 at 5.1 m) stayed in Play.
- #7 side shot weak: -watch Dog:Hunt shows the dog side-on with no hare in frame; -huntside 6 locked onto a Wolf.
- #16 NOT met in the build: village children (21.8/17.0 kg, tame, leash 68 m) walked off on all 3 carries. Held fine
  in the editor (19.6 kg, Carry pose). Likely the 25% walk-off for tame non-friendly creatures in Interactions.Apply
  (seeded from s.Seed, so the same child always refuses). Next: calm flag on InteractionRequest (Apply is Burst).

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

## Unverified (merged anyway; Zach's check in the build)
- #41 (A-10c, ae5b491): head glow fixed; host-to-client lunge not shown. Plant shove never landed in a test.
- #8 Z-52: live folk lines seen only twice.

## Open
- #12: numbers in (A-16); Zach's look. #33: sideways still 0 m/s on stick right.
- #7: side-on dog-and-hare shot; #16: calm child carry in the build (see A-20). #6, #43, #49: Zach's look.
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
