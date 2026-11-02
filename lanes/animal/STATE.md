# Animal lane: state (2026-11-02, after batch A-01)

Worktree `Punchy` (main PC; only the animal lane writes there). `animal-behaviours` = origin 4cb6ba3 (v0.11.0 +
docs split), unchanged. The tree is checked out on `wip/a01-p3`.

## In flight: P3 built, not merged
`wip/a01-p3` a1bc1ef = 4cb6ba3 + merge of wip/animal-p3 6a52fd1 (61529a4, clean, CRLF kept) + test hooks
(`-voicelog`, `-stancelog`, FaunaDirector.BlobAt). Compiles clean, player builds with 0 errors, 0 exceptions in a
330 s shot-list run. Not on animal-behaviours yet because three rows aren't fully verified and all four sit in one merge.
- #9 Z-53 chases: PASS on numbers (28 pounces, 9 landed, 7 ate, 1 slipped).
- #8 Z-52 stances: odds table matches the approved one (toward Elf: Dwarf Rival, Human 32% wary / 20% annoyed,
  Goblin/Ogre neutral). Live lines unchecked: no folk lines at all in 25 s at DwarfHold, ElfGrove or HumanVillage.
- #7 Z-51 voices: Yowl and Whine seen; Hiss, Growl and DogHowl not triggered in this run.
- #6 Z-50 gallop: not checked. The -watch camera is 15-20 m off; it needs a close -camlook side view.
**Next slot:** a short second run on the same build (no code change needed unless lines are broken): close side
view of a sprinting coyote/wolf; dog spawned next to a cat (hiss/growl); a 3-4 min night at timescale 4 (DogHowl);
stand inside a town square for folk lines. Then merge wip/a01-p3 into animal-behaviours and push.

## Test notes (move to TESTING.md when there's time)
- A shot list needs `-shotdir` on the launch line too (ShotList waits on it before applying line 1).
- Per-line flags (-huntlog, -spincheck) reset on every shot-list line: repeat them on each line.
- `-voicelog` logs cat/dog voices and howls; `-stancelog` logs folk lines with stance plus the odds table.
- In PowerShell, `unity command eval` loses double quotes: use the Bash tool for eval code with strings.

## Open, not started
- #33 climbing relative to the surface (A-01 row 5, not reached: budget).
- #11 Z-21 long-stride foot bend (Zach to check in v0.11.0). #12 dormouse spins (2 seen in this run).
- #16 carrying large animals. #10 new species: fish folk, blind cave pygmies (needs design).
