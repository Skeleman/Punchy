# Long-term ideas (Zach)

Not scheduled. The supervisor adds Zach's long-range ideas here; lanes pull from it when planning past the
current release. Lane: A = animal, W = world.

## Creatures
- (A) Rare gold variants of creatures. (2026-10-29)

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
