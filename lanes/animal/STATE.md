# Animal lane: state (2026-11-02, after batch A-03)

Worktree `Punchy` (main PC; only the animal lane writes there). `animal-behaviours` = origin c52d716: P3 (Z-50..53)
merged on the numbers, plus A-03 (#33 climb stick, #43 ambient sounds per biome, 3D fear distance). Tree is on
animal-behaviours; Builds/Windows is the A-03 build. No Punchy.exe or editor of the lane is running.

## Unverified (merged anyway; Zach's check in the build)
- #6 Z-50 gallop: never framed. Coyote resting in the A-03 shot. Needs a hook (shoot when a hunter's Speed > 0.9 x RunSpeed).
- #7 Z-51: Hiss(49), Growl(52) not heard in two runs. Try Dog and Cat spawned 4 m apart, or Dog next to Wolf.
- #8 Z-52: live folk lines seen only twice (Dwarf Rival to an Elf body, with `-spawn Dwarf:3 -spawn Human:3`).
- #33 wall climb (Lizard) and a remote climber on a client: not run. Seed 1337 has no ledge for `-place ledge` near start.
- #43: rate set by budget (0.6/s, was 1.5/s); no played-calls-per-minute count yet.

## Done this batch
- #33 trunk: forward up, back down, right along it (StickOnSurface); verified with Monkey, Oak: 4 up/down cycles in 70 s.
- #43: AmbientSounds table by biome NameHash. Silent: Glacier, SaltFlat, Ashlands, ElectricWastelands, GlassDesert, Ocean.
- 3D fear distance: hover 5/15/30 m over deer, fleeing within 40 m 1-2 / 0-5 / 0 (no baseline). File a baseline if Zach still sees it.

## Test notes (move to TESTING.md when there's time)
- Shot list needs `-shotdir` on the launch line. Launch with `-quit`: the player closes itself when the list ends.
- Per-line flags reset each line: -huntlog, -voicelog, -stancelog, -lift h (player held h m over the ground, logs fleeing counts).
- Climb test: `-autowalk -body Monkey` plus `-place tree:Oak` (TestCamera keeps the camera on the trunk so back reads as down).
- Opening the editor: `unity open .`, wait for `unity status` ready, `unity recompile`; build with BuildPlayer via eval
  (it times out at 30 s but finishes: wait for a Temp file); `unity close .` afterwards.
- `-body Elf` for stances covers Rival, Kin, Wary, Annoyed. In PowerShell `unity command eval` loses quotes: use Bash.
- Artefacts: PunchyMilestones/animal/a03/ (shots.txt, run.log, gallery/, climb/).

## Open, not started
- #11 Z-21 foot bend. #12 dormouse spins. #16 carrying large animals. #10 new species (needs design).
- New: animals still flee a flyer high above (A-03 made the distance 3D; confirm with Zach before filing more).
