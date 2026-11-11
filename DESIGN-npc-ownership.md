# NPC homes, households and ownership (Zach 11-10, future direction)

Not a card yet. Written down so the player and animal lanes build toward it; it gets carded once interiors (P-25),
pickups and inventory (#26) and folk talk (A-93, P-26) are on main.

## What Zach wants
- **Houses belong to people.** Each house is lived in by specific folk: sometimes a family, sometimes one person.
- **Items belong to people.** Things inside a house are owned by whoever lives there.
- **Folk go home.** Folk who work outside (fields, piers, shops) go back to *their own* house at night, not to the
  nearest door.
- **Family talk.** Folk may have lines about their own family members (spouse, children, parents), by name.
- **Theft.** Taking an owned item upsets the owner (and family) if they notice. Guards may confront you as a thief.

## Where things stand (main, 11-10)
- Folk have a `Home` point and pick the nearest Door anchor within 8 m (CreatureBrainSystem.Folk.cs); a hermit's home
  is its shelter, a traveller's is a camp. There is no house identity, household or family link.
- FolkPersona (A-93) gives each folk stable traits from its Individual hash, with nothing sent over the network or stored.
- Interiors and doors (P-25, player/integrate) and the P-28 gear are not merged yet. There is no ownership or theft.

## Shape (to settle when carding)
- **Household, derived like FolkPersona:** from a house's placement hash, draw its household (size, family roles) and
  give each member that house as Home. Then every machine agrees, with nothing sent and nothing stored. It must not
  touch the spawn draws (fingerprint pin) or landmark placement; warn Zach before changing either.
- **Names and family lines:** names come from the household draw; dialogue.json gets `family` lines keyed on role
  (e.g. "my daughter {name} ...").
- **Ownership:** interior items carry an owner tag (household id, or "nobody" for wild/loot items). Owner is derived,
  not stored per item.
- **Theft:** a pickup of an owned item is a crime if any household member or guard sees it (reuse the senses code).
  Effects: the owner's reaction (dialogue, mood or trust drop, refuses to talk), and a guard confronting you (pay, return the item, or
  run). Needs a small per-player record that is saved and synced (stolen items and how known you are as a thief).
- **Multiplayer:** the host decides who saw a theft; the record syncs like P-28 gear.

## Lanes
- Animal lane: households, going home to your own house, family lines, guards' reaction.
- Player lane: item owner tags, pickup and theft check, the per-player record, UI hints (red hand on owned items).
- World lane: only if houses need a stable id or bed and room anchors per occupant.
