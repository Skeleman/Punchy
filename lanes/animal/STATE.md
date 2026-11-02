# Animal lane: state (2026-11-02)

Worktree `Punchy` (main PC; only the animal lane writes there), branch `animal-behaviours` c810406.
Branch head 2026-11-02: 4cb6ba3 (v0.11.0 tree + docs split; both lanes start here).
Backlog: `gh issue list -l lane:animal`. Released: v0.11.0 includes c810406 (Z-20 InteractRpc fix).

## In flight: P3 rows, one combined branch, nothing compiled yet
`wip/animal-p3` 6a52fd1 = c810406 + the four rows below (one conflict in CreatureData.cs kept both sides; CRLF
kept in CreatureData.cs and CreatureSpecies.cs). Design notes approved in DESIGN-animal-P3.md.
- #9 Z-53 chases (c0a9188): prey can be eaten (Zach).
- #8 Z-52 race stances (f88e15e): human stances both ways; Ogre confirmed; fish folk neutral; cave pygmies a
  little wary of all.
- #7 Z-51 voices (c382728): SoundId 49-53 appended (Hiss, Yowl, Whine, Growl, DogHowl); wolf Howl parameterised,
  same sound. Cat hisses bolting within 6 m and yowls at night; dog growls, whines, howls.
- #6 Z-50 gallop (c0b26df).
**Next slot:** compile, build, one shot session covering all four, merge what passes.

## Open, not started
- #11 Z-21 long-stride run bends the foot too steeply (may already be fixed in v0.11.0: Zach to check in play).
- #12 Z-07 leftover: songbird and dormouse spins while wandering (low priority).
- #16 Z-20 follow-up: carrying a large animal (dogs mostly over the 0.65 m limit; wild tortoise squirms free).
- #10 new species: fish folk and blind cave pygmies (needs design). Roadmap: #25 NPCs.
