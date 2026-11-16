# Design and code rules (9, 27, 28, 33)

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

## One system per kind of thing (rule 33, Zach 11-15)
The world follows one set of rules, so anything that shows up in more than one place shares one system: the same
code, assets and definitions. An improvement to the system then reaches every place that uses it.
- Already consolidated (extend these, never fork them): LOD (DetailPolicy screen size), water (surface, caves,
  landmarks, set pieces), materials (items, folk, creatures, props, colossi; W-166), effects such as fire (rule 30),
  climbing (ledges, caves, cliffs, ladders, colossi; WorldClimbs).
- When a card adds something, it names what that thing parallels. When one member of a family gains a feature,
  the card covers the whole family or names the follow-up card. For example: a galleon is a boat, so if folk can
  pilot it, small boats are pilotable too. Readable shop signs mean inn and other building signs become readable.
  Pickable plants make garden plants pickable. See-through windows on one building mean every building's windows.
  Folk sleeping in beds means floors, mats and benches are considered as sleeping places.
- Units are built from parts (Zach 11-15, W-175): a stall, tent or camp lists standalone parts (awning, table,
  anvil) defined once, so other units reuse them and a part change reaches every instance.
- Stand-ins follow the definition. Once a thing gets a real definition (paper, rope, glass), whatever imitated it
  moves onto that definition.
- Design the first member for the family: data-driven kinds, not hard-coded cases. A card that finds a parallel
  it can't fold in reports it in its handoff (rule 28's "report, don't add a third").

## Every feature works solo, as host and as a joined client
Decide which kind of state it is with `docs/agents/multiplayer.md` (main). New wire data is a rule 11 ask.
