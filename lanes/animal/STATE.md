# Animal lane: state (2026-11-02, after batch A-02)

Worktree `Punchy` (main PC; only the animal lane writes there). `animal-behaviours` = origin 4cb6ba3 (v0.11.0 +
docs split), unchanged. The tree is on `wip/a01-p3` a1bc1ef (P3 built; player build in Builds/Windows is current).

## In flight: P3 built, still not merged
Two runs (A-01, A-02) on the same build. 0 exceptions in both. Rows:
- #9 Z-53 chases: PASS (A-01: 28 pounces, 9 landed, 7 ate; A-02 saw stalk, run, pounce missed, tired).
- #8 Z-52 stances: odds table PASS (Elf body: Dwarf Rival 100%, Elf Kin 100%, Human 48/32/20). Live lines still
  unchecked: 0 folk lines in 70 s at each of DwarfHold, ElfGrove, HumanVillage. FolkTalk Reach is 5 m and
  `-place` puts the player at the landmark, not beside folk. Fix for the test: add `-spawn Dwarf:3 -spawn Human:3`
  (wild spawn lands near the player) on the town line, or `-goto` a street and wait.
- #7 Z-51 voices: seen Bark(27), Whine(51), Yowl(50, cat), DogHowl(53, night, timescale 4, 210 s). Not seen:
  Hiss(49), Growl(52) in 80 s of 3 dogs + 3 cats in the meadow. Needs a cat bolting from a dog within 6 m, or a
  dog backing off a threat within 8 m: try dog and cat spawned on one line with `-wait 90`, or Dog next to Wolf.
- #6 Z-50 gallop: NOT checked. `-watch Coyote:Hunt -watchfrom 1,5` framed only the player (no coyote with
  Task Hunt picked, so the camera never left the player). Use `-watch Coyote` (no task) with `-watchfrom 1,5`,
  and take 3-4 shots on separate lines with different `-wait`; or add a test hook (in the next build) that
  shoots when a hunter's Speed > 0.9 x RunSpeed.
**Next slot:** same build, one list: gallop with `-watch Coyote -watchfrom`, hiss/growl line, towns with spawned
folk. Then merge wip/a01-p3 into animal-behaviours and push. A third unverified run: merge on the numbers
(odds table, hunts, voices 50/51/53) and leave #6 and the live lines as Zach's check in the build.

## Test notes (move to TESTING.md when there's time)
- A shot list needs `-shotdir` on the launch line too (ShotList waits on it before applying line 1).
- Per-line flags (-huntlog, -spincheck) reset on every shot-list line; spawns and watch reset too.
- `-voicelog` logs voices and howls; `-stancelog` logs folk lines with stance plus the odds table.
- The player build doesn't quit when the list ends; `-logFile` goes where you point it. Closing it needs a
  permitted stop (the classifier denied Stop-Process in A-02: PID 31528 may still be open, close it by hand).
- `-body Elf` for stances covers Rival, Kin, Wary, Annoyed in one run. In PowerShell `unity command eval` loses
  double quotes: use Bash.
- Run artefacts: PunchyMilestones/animal/p3-a02/ (shots.txt, run.log, gallery/).

## Open, not started
- #33 climbing relative to the surface. #43 ambient sounds per biome. #11 Z-21 foot bend. #12 dormouse spins.
- #16 carrying large animals. #10 new species: fish folk, blind cave pygmies (needs design).
