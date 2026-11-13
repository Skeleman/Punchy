# Roadmap ideas (Zach)

Zach's long-range ideas, paraphrased; one entry per idea. His own wording is on the issue named. The supervisor
keeps this file; lanes read it only when a card names a section. Before carding an entry, check the issue,
`lanes/ISSUES-MAP.md`, cards in flight and main.

Standing: the aim and its priorities are in COORDINATION.md. The layout is a test bed and may change
(`lanes/shared/world-data.md`). Core tenet: small, varied, seeded details everywhere, kept sparse.

## World and far view
- **Far views (#298).** The whole island in sight: ground to the horizon, far forest, far life. Cards R-22, R-23,
  A-108, A-109.
- **Haze by weather (#298, R-22 row 2).** Cloudier and more humid days are hazier; heavy storms far more so.
  Approved 11-13.
- **Cliffs (#281, W-155, DESIGN-cliffs.md).** Cliffs built as the ground itself, not pieces on a slope: mountain
  edges, canyons, road and river cuts, washes, city terraces. Some shelves hold settlements, hermit homes, ruins
  or flora, for the views.
- **Rivers (#301, W-153).** No kinks, dead ends or water walls. Reorder the generator if routing is the cause.
- **Waterfalls (#284, R-21).** One builder for every shape and angle of fall; a subdivided mesh with gentle,
  uneven curves; vertex displacement for turbulence at the lip and ripples at the base.
- **Lava and toxic pools (#303, W-154).** Real liquids with their own viscosity; a better lava look.
- **Lights (#302, R-25).** No light switches on as you approach.
- **Shadows (#286, R-20).** No flicker. 4096 main shadow map approved.
- **Stylized realism (#304, R-26).** Less cartoon in bark and cave cracks; cave and terrain detail should match.
- **Floating islands (#256).** Off the coast, walkable, some with caves, one with a new kind of town. Far islands
  draw simply, with collision only near the player. Reached by flying for now.
- **World file (#260).** One day the island is generated once, saved, hand-edited and loaded. Far off; rule 22
  keeps code ready.
- **Plant fidelity (#21)** experiments. **Cluster culling (#270)** waits for profiler numbers; individual
  billboards come first.

## Settlements
- **Castle walls (#254).** Modular walls, towers, a gatehouse with a working, synced portcullis, interior halls.
  Rings a city or stands alone; the ruined form is the "old fort" dungeon.
- **Port town (#255).** One dock town gets a large pier, a boardable galleon (deck, cabins, hold) and a busy main
  road with stalls and carts.
- **Sea folk city (#37).** Built and populated (A-103). Next: far town life (A-109).

## Folk and races
- **Playable races (#291).** Sea folk, bat folk and turtle folk. Bat folk see everything brighter: caves easy,
  the surface hard, and glasses give them normal sight. Other races get the bat view from glow goggles. Sea folk
  never run out of breath; on land they keep near water or complain about the dryness. Turtle folk are tall,
  ancient and magical.
- **Breath (A-102, P-34).** Works like stamina; air breathers are forced to resurface.
- **One population (#298, A-110).** A town's far figures are its real people, in about the real number, on
  simple low-rate brains; nobody vanishes or is swapped as you approach.
- **Households and ownership (DESIGN-npc-ownership.md).** Specific folk live in each house, go home to it, and
  speak of family by name. Later, after P-35: owned items, and theft that angers owners and guards.
- **Conversation (#25).** What an NPC says depends on personality, skills and culture. First: short lines as you
  pass. Emotes start a conversation and are how you interact with animals. Later: dialogue options. Text lives in
  a hand-editable archive outside the build, reloaded live, with fill-in slots filled from the NPC's stats,
  culture and background.
- **Skills and attributes.** A small shared template for players and NPCs that grows as features need it.
  Humanoids start at climb skill 2.

## Creatures
- **Sand and snow worms.** Huge worms that travel underground (a ripple or trail) and burst out in dunes and
  snowfields. Needs a long segmented body, a ground-disturbance effect and surfacing rules.
- **Riding (#278, A-104).** Any creature may ride one at least 50% heavier. A ridden player keeps control.
- **Climbable colossi (#276).** Sections graded on the shared climb scale, perhaps a fur section; the climber is
  carried as the colossus moves.
- **Gold variants.** Shipped: 1 in 400.

## Dungeons, items, quests
- **Dungeons (#24).** Caves and ruins as sprawling corridors and rooms. Ruin cultures: old forts, ancient temples,
  a fallen sky civilisation, sand temples, salt-people temples in the salt flats. Two phases: set pieces (player
  lane), then random spawn sockets for creatures and items (animal lanes). Groundwork: synced interactive objects
  (gates, doors, torches), interiors with room or portal culling, room-and-corridor generation from cave networks.
- **Items and inventory (#26, P-35).** Player and container inventories; moved objects keep their place, saved and
  synced, without bloating the world. Plants, animals, crystals and ore yield items; harvestable flora is tagged
  apart from clutter. One item definition and ID scheme, designed once (rule 27).
- **Quests (#27).** Built from objective types: fetch and deliver, clear creatures from an area, lead an NPC or
  animal somewhere, find a rare item. Needs items, places and dialogue; text shares the dialogue archive; progress
  is synced.

## Seasons
- A generic calendar: day-of-year counter, configurable year length and season count, synced. Values follow
  curves over the year and never jump at a boundary.
- Global: sun and moon arcs and day length change through the year.
- Per biome, a profile by day of year: temperature offset, weather odds, flora look (colour, leaf drop, bloom,
  snow, dormancy), fauna spawn numbers, water (desert rivers dry most of the year and fill in a monsoon; frozen
  lakes, spring floods, shrinking ponds).
- Animals and folk never read the season. Their behaviour changes only through light, temperature and storms
  (builds on A-71).

## Fire
- One shared fire effect with a colour setting, used by everything that burns; Ember Forest burns one seeded
  colour per night. Shipped (#131) with 4 colours of the 7 asked for (orange, golden, silver, violet, emerald,
  crimson, black). Zach wants further tweaks later.
