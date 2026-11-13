# Long-term ideas (Zach)

Not scheduled. The supervisor adds Zach's long-range ideas here; lanes pull from it when planning past the
current release. Lane: A = animal, W = world.

## Creatures
- (A) Rare gold variants of creatures. (2026-10-29) Done for v0.10 (1 in 400).
- (A/W) Sand worm and snow worm (2026-10-30): enormous worms that usually move underground (a ripple or trail
  in the sand or snow) and periodically burst out in flat, duney areas (desert and glass desert dunes, snowfields).
  Needs a long segmented body, a ground-disturbance effect from the world side, and rules for where they may surface.
- Climbable colossi (2026-11-12): sections of a colossus graded on the shared climbability scale (A-101), maybe a fur section to climb; the climb rides the colossus as it moves (#276).
- Rideable creatures (2026-11-12): any creature can ride another at least 50% heavier than itself (rider mass x 1.5 <= mount mass), so a child can ride an adult, a cat a dog. Players can ride each other in multiplayer: the one being ridden keeps control, the rider is carried (#278).

## Dungeons: caves and ruins as expansive dungeons (2026-10-29)
- Ruin types from different ancient cultures:
  - old forts;
  - ancient temples;
  - a sky civilisation's ruins that fell to the earth;
  - ancient sand temples;
  - salt-people temples in the salt flats.
- They can start big, but the goal is sprawling corridors and rooms.
- Groundwork that helps, in rough order:
  - (W/A) Interactive world objects: gates and doors that open and close, torches you light and put out. Their state
    is synced in multiplayer.
  - (W) Basic interiors (already on the roadmap), with smart culling: portals or rooms, so only what's visible draws.
  - (W) Room-and-corridor layout generation, building on cave networks and anchors.

## NPCs and characters (2026-10-29)
- (A) NPC conversations: what an NPC says depends on its personality, skills and culture modifiers.
  - Stage 1: short lines an NPC says as you walk by, shown as text.
  - Emotes start a conversation, and are also how you interact with animals.
  - Later: dialogue options.
  - The text lives in an archive outside the build that Zach can edit by hand (for example JSON, YAML or CSV in
    StreamingAssets, reloaded live).
  - Lines use fill-in-the-blank slots ({culture_greeting}, {trade}, {home_town}...) that are filled from that NPC's
    stats, culture and background, with per-culture word lists.
- (A) Character skills and attributes: start with a small template (health, a few attributes) that grows as features
  make skills relevant. Shared by players and NPCs.

## Items and inventory (2026-10-29)
- Items you can pick up, which are tracked and stored:
  - a player inventory;
  - object inventories (chests, carts, shelves);
  - smart management of objects that can be moved permanently: they keep their position and are saved and synced,
    without bloating the world.
- (W/A) Every plant and animal yields items, as do crystals and ore veins.
  - (W) Tag flora so harvestable plants are told apart from clutter such as rocks and debris.
- Note: items, inventory and harvesting share a single item definition (asset) and an ID scheme. That should be
  designed once, before either lane builds on it, and probably counts as a network table change (rule 11).

## Quests (2026-10-29)
- A quest system built from objective types:
  - fetch item X and bring it to place Y;
  - make Z creatures leave an area;
  - lead an NPC or animal to a special place;
  - find a rare item.
- Depends on items/inventory, anchors (places) and NPC dialogue (who gives the quest). Quest text can use the same
  editable archive and fill-in slots as dialogue. Progress is synced in multiplayer.

## Fire revamp (2026-11-04)

- Ember Forest catches fire at night. Each night it burns in one colour, picked at random (seeded per night, the same for
  every player): orange (standard, the most common), golden, silver, violet, emerald, crimson, black.
- One standard fire effect (flame, glow, light, embers, smoke) with a colour setting, used by everything that burns:
  torches, campfires, ruin sconces, braziers, the forest. Build the shared effect first, then move the existing fires onto it.
- 2026-11-05: both parts shipped (#131 closed, PRs #152, #153). Zach: passable for now, wants to tweak fire further later.
  The forest uses 4 colours (Wood, Blue, Green, Violet), not the 7 listed above.

## Seasons (long term, 2026-11-04)

- Generic, not tied to Earth: a world calendar with a day-of-year counter and a configurable year length and number of
  seasons. Values change gradually day by day (curves over the year), never jumping at a season boundary. Synced in multiplayer.
- Global: the sun and moon arcs (height, rising and setting points) and the length of day versus night change across the year.
- Per biome, a season profile driven by the day of the year:
  - temperature offset;
  - weather odds (rain, snow, storms, fog, wind);
  - flora appearance (colour shifts, leaf drop, bloom, snow cover, dormant plants);
  - fauna spawn rates (which species appear, and how many). Spawn numbers only, no behaviour.
  - water: desert rivers are dry beds most of the year (cracked mud, stranded driftwood) and flow in a monsoon season,
    filling gradually. Other seasonal water could follow the same idea (frozen lakes, spring floods, shrinking ponds).
- No season-driven tasks or scripted seasonal behaviour for animals or humanoids. They never read the season. Any change
  in what they do emerges only from their response to the environment it changes: light, temperature and storms.
  This builds on the weather comfort work (A-71).

## Settlements and places (2026-11-10)
- (W) Castle walls (#254): modular wall segments, towers and a gatehouse with a working portcullis (state synced in
  multiplayer), plus interior halls. Used to ring a city or as a standalone fort. A ruined version covers the
  "old forts" dungeon type.
- (W/A) Port town (#255): one dock town gets a larger pier with a galleon you can board (deck, cabins, hold), and a
  bustling main road (more folk, stalls, carts). Builds on #36 and the pier networks.
- (W) Floating islands (#256): off the coast over the ocean, walkable, some with caves inside; one carries a new kind
  of town. Asked for before (11-09 audit, W-113 "floating archipelago") but never logged until now. Far islands
  draw simply; collision only near the player. Reached by flying for now (Zach 11-10).
- (W) Cliff and mountain geometry beyond the heightmap (#281, 2026-11-12): big separate rock pieces on cliffs and
  mountain faces with real texture, strata, overhangs and ledges, which the heightmap can't do. Ties into climbing
  (grades, shimmy ledges) and gives shelves for mountaintop settlements and ruins. Design study first (W-149).
  Zach 11-12: shelves can hold settlements, hermit homes, ruins, even flora; not all, but some, for the views.
  Carded: R-19 (triplanar rock on steep ground), W-150 (cliff kit, shelves offered to places).


## Current roadmap (snapshot 2026-11-12, from open GitHub issues)

Done from Zach's 2026-11-03 order: the modular kit and colossi (#40), fishing villages (#36), bat folk and cave
pygmies (#35, #10), turtle folk (#38). Core rule throughout: small, varied, seeded details everywhere, kept sparse.

### Open, not yet started
- Sea folk underwater city in the central lake (#37): the city, landing pier and road exist; nobody lives there yet (A-103).
  Zach 11-12: the next big item. NPCs swim under water; a breath meter that works like stamina; sea folk never use breath
  under water; NPCs that do are forced to resurface. Carded: A-102 (diving, breath), P-34 (meter).
  Zach 11-12: the city already exists, it's just not populated; the turtle folk igloos in the mountains are empty too.
  Carded: A-103 (turtle folk and sea folk races, residents in the igloos and shell domes).
  Zach 11-12: "Seafolk and bat folk should both be playable. The bat folk should see everything brighter, making caves easy but the surface difficult." #291, carded A-103 rows 4-5.
  Zach 11-12: "Turtle folk are also playable." Added to A-103 row 4.
  Zach 11-12: "The bat folk can put on glasses to see normally on the surface." Added to A-103 row 5.
  Zach 11-12: the turtle folk are tall, ancient, and magical (A-103 row 1).
- Castle walls: modular walls, towers, a working portcullis, interior halls (#254).
- Dock town with a big pier, a boardable galleon and a busy main road (#255).
- Floating islands off the coast, walkable, with caves and a new kind of town (#256).
- Climbable colossi, maybe a fur section (#276), after unified climbing (A-101, W-144, W-147).
- Rideable creatures: anything rides a creature 50% heavier; the ridden player steers (#278).
- Cliff and mountain geometry: overhangs, ledges, strata as separate rock pieces (#281); W-149 done, R-19 and W-150 carded.

### Open, partly done
- Dungeons (#24), NPCs (#25), items and gear (#26), quests (#27): player lane cards in flight.
- Plant fidelity experiments (#21).
- Cluster culling (#270): waits for profiler numbers.

Before carding any of these, check the issue, lanes/ISSUES-MAP.md, in-flight cards and main so it isn't already done.
