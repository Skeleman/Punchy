# Long-term ideas (Zach)

Not scheduled. The supervisor adds Zach's long-range ideas here; lanes pull from it when planning past the
current release. Lane: A = animal, W = world.

## Creatures
- (A) Rare gold variants of creatures. (2026-10-29) Done for v0.10 (1 in 400).
- (A/W) Sand worm and snow worm (2026-10-30): enormous worms that usually move underground (a ripple or trail
  in the sand or snow) and periodically burst out in flat, duney areas (desert and glass desert dunes, snowfields).
  Needs a long segmented body, a ground-disturbance effect from the world side, and rules for where they may surface.

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

## Current roadmap (snapshot 2026-11-04, from open GitHub issues and Zach's direction)

Zach's order (2026-11-03): (1) a modular building kit plus more colossi, (2) fishing villages built from the kit,
(3) new settlements with their races. Core rule throughout: small, varied, seeded details everywhere, kept sparse.

### In progress or queued now
- (A) Bat folk look: no hat-like hair, bigger ears, a lighter coat; fires and torches count as light (#35, A-68 row 2).
- (A) Blind cave pygmies live with the bat folk in the same chamber, in a symbiotic setup (#35, A-68 row 3).
- (W) Bat folk homes are organic pods hung from the ceiling; pygmy homes are cutouts in the cave walls (#35, W-96).
- (W) Play-mode test harness that every lane runs before asking for a merge (W-97).
- (W) Salt ball as a meteoroid: higher-poly irregular body, smaller crystals, a trail that varies in width (#40, W-95 row 3).
- (W) Fire revamp: one shared fire effect for everything that burns; Ember Forest burns at night in a seeded colour (#131).
- (A) Fishing villages: the brain still spends about 2.5 ms on solids checks; next is a BuildingSink precheck (#132).

### World (W)
- Colossi: one unique giant creature per biome, built to Zach's per-biome design in the issue (#40).
- Shelters and work suited to each biome, for hermits and harsh-biome settlements; the Tundra shelter has not shown up yet (#69).
- Electric wastelands: the player still runs through some flat rock chunks (#70).
- Ruins: old stone-brick walls and a natural wind-shaped sand mound (#80).
- Water geography rethink: stepped pools and the open water issues, redone rather than patched (#83).
- Roads: cobblestone, paved stone settlements, tunnels (#39).
- Sea folk: an underwater settlement in the central lake (#37).
- Dungeons: caves and ruins as large dungeons, in two phases. First, set pieces made from underground tilesets
  (player lane, plus the socket contract). Second, spawn points filled at random with creatures, chests and clutter
  per biome and dungeon type (animal lane). The world lane adds entrances and tile art later (#24).
- Interactive doors, gates and torches whose state syncs in multiplayer; interiors with portal culling (see Dungeons above).

### Creatures and NPCs (A)
- Decide as one activity table with explicit priorities (#60).
- NPC reactions to the player's race (#8).
- NPCs and characters: personality-driven lines, then dialogue options; shared skills and attributes (#25).
- Sand worm and snow worm (see Creatures above).

### Player, items and quests (P)
- Graphics settings: resolution and full screen (#78).
- Camera underground: clipping in ruins and vertical control (#85).
- Items and inventory: player and object inventories, picking up and moving objects, harvest yields (#26).
- Quests: more objective types on the shipped base: lead an NPC, find a rare item (#27).

### Tech, performance and platforms
- Startup: one 16 s main-thread frame after the island is ready (#102).
- MonoBehaviours driving ECS: explicit order and job dependencies (#62).
- Underwater camera, visuals and sound (#22, Z-64).
- Plant fidelity experiments (#21, Z-63).
- Linux build (#28).
