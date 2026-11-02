# Animal lane: state (2026-11-02, after batch A-23)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin 5557c9a (A-23).
Builds/Windows is the A-23 build. Editor open on port 7800; no Punchy.exe of ours running.

## A-23 (artefacts PunchyMilestones/animal/a23/)
- #6 met: `-huntside d` places the camera d m perpendicular to the hunter's Yaw every frame (was the hunter-to-prey
  line, which ran 157-162 deg off the heading when the nearest "prey" was behind the wolf). leapgather-800 (gather
  1.00) and leapextend2-800 (gather 0.00) are both side-on at 4 m, wolf at 12.3 m/s. Gait values untouched.
- #7 met: `-huntpair` aims between hunter and prey (steps back past 1.6 d apart). dogchase-800: dog and
  SnowshoeHare 2.1 m apart, 5 m side-on, both clear.
- Back-to-back -shotonhunt lines skip the previous line's hunter until its sprint ends (staleHunter); without it
  the extend shot caught the slowing wolf walking.
- huntside log line now gives "prey line N deg off the heading".

## A-22 (artefacts PunchyMilestones/animal/a22/)
- #23: cave cull now a Burst job: 0.03-0.04 ms (was 1.6-2.1 ms); system total 1.9 -> 0.33 ms (100 creatures).
  Not checked on v2 (not in this tree): run `-renderpasslog` with and without `-oldcull` there.
- Flags: `-renderpasslog` (ms per pass every 3 s), `-oldcull`. `-incave` goes on the line after `-goto`.

## A-21 (artefacts PunchyMilestones/animal/a21/)
- #6 GallopStyle.Leap: Wolf, Cat, Dog (Coyote as Wolf variant); Deer, Horse, Bison, Pronghorn, BlackBear Rotary;
  small animals Bound (-gaitlog).
- #16 met: walk-off rolled per attempt; settled village child never walks off (childcarry-800).
- Flags: `-leapphase p` (freeze leap phase), `-hunter Species`, `-gaitlog`.

## A-19/A-20
- #50 met: CreaturePersonality from the replicated Individual (`-personalitylog Dog`).
- #7: FaunaDirector.Lists matches Hunts by name (Resources copies broke reference match).
- #16: weights in kg in all 113 species, CarryCapacity 40 kg.

## Unverified (merged anyway; Zach's check in the build)
- #41 (A-10c, ae5b491): head glow fixed; host-to-client lunge not shown. Plant shove never landed in a test.

## Open
- #12: numbers in (A-16); Zach's look. #33: sideways still 0 m/s on stick right.
- #6, #7, #16, #43, #49: Zach's look. #23: confirm on v2.
- #10 new species (needs design).
- Animals still flee a flyer high above (confirm with Zach before filing more).

## Test notes
- Shot list needs `-shotdir` (the folder must exist, or shots are silently skipped); launch with `-quit` (add
  `-autowalk` only for climb tests; it scares songbirds). `-spincheck` goes on the shot-list line.
- `-spincheck` logs about every 30 s; songbird windows count all songbirds nearby: spawn 16 for 25 s+ windows.
- `-shotonhunt` brings the line's own shot forward (file is <line>.png). `-huntside d` rides side-on 1 s into the
  sprint; add `-huntpair` for two animals. `-weather` forces at the player's spot: put `-goto` on an earlier line.
- Shot lists survive the client's scene reload: a second line runs on in the solo game (A-18 #49).
- `-actiontest roll:A,B|fly:Body|fish:Trout`; `-setbody X`; `-place wall`; `-place clearlake`;
  climb test `-autowalk -body Monkey -place tree:Oak`. Client roll check: see TESTING.md.
- Editor: `unity recompile` (no --port); BuildPlayer via `unity command --port 7800 eval "$(cat Temp/a23_build.cs)"`
  from Git Bash (PowerShell strips the quotes); times out at 30 s but finishes, ~2-3 min; wait for Temp/a23build.txt.
