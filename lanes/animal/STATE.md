# Animal lane: state (2026-11-02, after batch A-25)

Worktree `Punchy` (animal lane only). `animal-behaviours` = origin a077715 (A-25); status clean apart from the old
untracked `Assets/_Recovery/` (from 10-30, not ours to delete).
Local branch `merge-test` ac8031a = 5557c9a + origin/world-features fb6d7ee (NOT pushed; one commit behind
animal-behaviours now). Builds/Windows is the A-25 build (animal-behaviours a077715, no `-island v2`).
Editor open on port 7800, on animal-behaviours, 0 compile errors.

## A-25 (artefacts PunchyMilestones/animal/a25/)
- #6: leap hip eased 30 deg off both extremes (CreatureAnimator.LeapHip, LeapHipEase 0.52 rad). Wolf upper leg
  from vertical, body frame: front gathered -91 -> -61, front extended 93 -> 64, hind gathered 102 -> 72,
  hind extended -63 -> -33. Measured with Temp/a25_angles.cs (editor eval, builds the Wolf blob).
- Shots leapgather-800 (10.7 m/s), leapextend2-800 (11.6 m/s, retake: the first fired at 6 m/s, part-blended).

## A-24 (artefacts PunchyMilestones/animal/a24/)
- Merge rehearsal: one conflict, docs/agents/rendering.md (kept both bullets). No scene/ProjectSettings changes.
- #23 confirmed on v2: CreatureRenderSystem 7.1-7.3 ms with `-oldcull`, 0.32-0.35 ms with Burst (135-145
  creatures). Hashes: v2 127 / 831FF2EE, v1 101 / E128CB1D.

## A-23
- #6 `-huntside d` sits d m off the hunter's Yaw; #7 `-huntpair` aims between hunter and prey.

## A-21/A-22
- #6 GallopStyle.Leap: Wolf, Cat, Dog (Coyote as Wolf variant); Deer, Horse, Bison, Pronghorn, BlackBear Rotary;
  small animals Bound. Flags: `-leapphase p`, `-hunter Species`, `-gaitlog`, `-renderpasslog`, `-oldcull`.
- #23: cave cull is a Burst job. #16 met: walk-off rolled per attempt.

## A-19/A-20
- #50 CreaturePersonality from the replicated Individual. #7 FaunaDirector.Lists matches Hunts by name.
- #16: weights in kg in all 113 species, CarryCapacity 40 kg.

## Unverified (merged anyway; Zach's check in the build)
- #41 (A-10c, ae5b491): head glow fixed; host-to-client lunge not shown. Plant shove never landed in a test.

## Open
- #6: Zach's look at the eased leap (a25 shots). #12: Zach's look. #33: sideways still 0 m/s on stick right.
- #7, #16, #43, #49: Zach's look. #23: v2 confirmed (A-24), Zach can close.
- #10 new species (needs design). Animals still flee a flyer high above (confirm with Zach first).

## Test notes
- Shot list needs `-shotdir` (folder must exist); launch with `-quit`. `-spincheck` goes on the shot-list line.
- `-shotonhunt` can fire before the sprint is up to speed: check `hunter speed` in the huntside log (want ~10+
  m/s for a full leap blend) and retake if low.
- `-weather` forces at the player's spot: put `-goto` on an earlier line.
- `unity recompile` with no --port may hit the world editor when ours is busy; prefer an eval on --port 7800.
- BuildPlayer via `unity command --port 7800 eval "$(cat Temp/a25_build.cs)"` from Git Bash; times out at 30 s
  but finishes in ~2-3 min; wait for Temp/a25build.txt.
