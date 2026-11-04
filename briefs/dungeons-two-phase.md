# Dungeons in two phases (issue #24), Zach 2026-11-03

## Zach's idea
1. **Spaces:** build dungeon rooms and halls from underground tilesets, the way temples are built from kit parts.
2. **Spawn points:** each space holds pre-placed points. At load, a random pick fills each point with content suited to the biome and dungeon type:
   - creatures;
   - chests, wall decorations, interactables and clutter.
   Creatures may act on what is near them, for example drinking at a pool or guarding a chest.
3. **Reuse:** the same set pieces show up in many places, and the spawn points make each copy different.

## Lanes (one owner per file)
- **Player lane: set pieces.**
  - Owns a new `Dungeon/` folder: room and hall set pieces, the layout that joins them, and the socket data below.
  - Uses the world kit and temple parts read-only.
  - A new tile shape goes to the world lane as a request, not as an edit.
- **Animal lane: spawning and behaviour.**
  - Owns the spawner: weighted tables keyed by biome + dungeon type + socket kind, one seeded pick per socket.
  - Owns the dungeon behaviours: guard, drink, roost, sleep, patrol, chosen from tags on the socket and its neighbours.
  - Uses the same spawner for items.
- **World lane:**
  - Placing dungeon entrances on the island (caves, ruins).
  - Any new kit or tile art.
  - Not needed for the first pass.

## Shared contract (owner: player lane; animal lane reads it)
`DungeonSocket` (IComponentData or a baked buffer element):
- `float3 Pos`, `quaternion Rot`
- `SocketKind Kind`: Creature, Chest, WallDecor, Interactable, Clutter
- `byte Size`: small, medium or large; this caps what fits
- `uint Tags`: bit flags such as NearWater, NearChest, NearAltar, Dark, Ledge, Doorway, Ceiling
- `uint Seed`: comes from the world seed + dungeon id + socket index, so every client makes the same picks
- `ushort DungeonType`, `byte Biome`

Rules:
- Changing the contract needs a heads-up in COORDINATION.md.
- The spawner must never write sockets. The set pieces must never pick content.

## First pass
- **Player:**
  - 3 set pieces: hall, room with a pool, dead end.
  - Joined into one test dungeon behind a debug teleport.
  - About 15 sockets of every kind.
- **Animal:**
  - The spawner fills the test dungeon from a small table: cave creatures plus simple chest and clutter props.
  - 2 behaviours driven by tags: drink at NearWater, guard at NearChest.
  - Same seed gives the same result on host and client.
