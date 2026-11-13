# Design and code rules (9, 27, 28)

## Details (core tenet)
Small, varied, seeded, surprising details everywhere, kept sparse (rivers: cattails, driftwood, overhangs). A
feature card usually ends with a details row.

## Everyone has a destination (rule 9)
NPCs, messenger birds and wandering animals always head for an identifiable goal: a door, well, field, seat, gate,
next town or landmark, water, lair, roost. No aimless random-point wandering over distance.

## Foundations before stand-ins (rule 27, P-35, #26)
Until the item and saved-state design is agreed with Zach, no card adds a new stand-in for an item, a worn thing,
ownership or state that must outlive the session (a key press that pretends to be an item, a host-only list that
should be saved). Existing stand-ins stay. A card that needs one says so in its handoff and stops at that row.

## Refactors have right of way (rule 28)
- A refactor card changes no look and no behaviour. It proves that with pixel-diffs or fingerprints and lists
  every intended difference.
- Refactor branches merge first; feature lanes rebase onto them. While a refactor row holds a file, feature rows
  that need it wait or take another row.
- No new code in a file over 2500 lines: add a partial or a new file beside it.
- A feature card that finds the same logic in two places reports it in its handoff instead of adding a third.

## Every feature works solo, as host and as a joined client
Decide which kind of state it is with `docs/agents/multiplayer.md` (main). New wire data is a rule 11 ask.
