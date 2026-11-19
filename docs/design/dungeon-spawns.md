# Dungeon spawns (#24 phase two, animal lane)

Design note, no code. Phase one (the rooms and their sockets) is `dungeon-pieces.md`. Zach's 11-03 idea,
paraphrased: pre-placed spots in every dungeon room are filled at load with a seeded pick of creatures, chests,
wall decor, interactables and clutter that suit the biome and dungeon type, so reused rooms never look alike.

Most of this exists already (A-50, A-80, A-91). Sections 1-3 write the built design down; sections 4-7 are what
is still missing, and section 8 proposes the rows.

## 1. Which rooms get which sockets

Sockets are written by the set pieces only (TempleSockets, CaveSockets); the spawner never writes one.

| Room | Creature | Chest | WallDecor | Interactable | Clutter |
|---|---|---|---|---|---|
| Temple hall (`IsHall`) | along it | - | walls | - | sides |
| Temple dead end (`IsDeadEnd`) | - | wall farthest from its doorway, NearChest | walls | - | - |
| Temple chamber | middle (Large from 40 m2); Ceiling when tall | corner chests (a second in big rooms) | four walls | pedestal or altar (NearAltar) | floor |
| Temple doorway | a guard on every doorway (Doorway) | - | - | - | - |
| Temple torch | - | - | - | each torch, to light or put out | - |
| Cave chamber | middle, Ceiling when tall | - | walls | - | floor ring |
| Cave dead end / main end | - | far wall (main end Large) | - | - | - |
| Cave passage | guard where it opens into a chamber (Passage, Doorway) | - | sides | - | sides |
| Cave pool / Lake room | rim spots, NearWater | - | - | - | rim |
| Buried ruin room (cave room kit) | as a cave room, tagged RuinRoom: picks from the Ruin rows | | | | |

- Caps per cave are 6/3/8/3/10 by kind, scaled with cave size up to three times; the temple thins to 15 per kind.
  Today: 28 dungeons, 836 sockets, 208 of them creature sockets (A-91 row 3 numbers).
- Tags carry the context the picks and behaviours read: NearWater, NearChest, Dark, Ceiling, Doorway, Passage,
  DeadEnd, Chamber. Bat folk chambers get no sockets (the bat folk own them).
- P-46 is moving two temple sockets out of walls. A pick reads the socket's kind, seed, size, type, tags and
  biome but never its position, so those two keep their picks unless their tags change; only the socket hash moves.

## 2. Seeded draws per socket

- Seed: `DungeonSocket.MakeSeed(world seed, dungeon id, socket index)`, a `math.hash(uint3)`. That hash does not
  avalanche, so neighbouring indexes give related seeds; every draw therefore goes through `SpawnTable.Scramble`
  first: the row roll is `Scramble(seed ^ 0x5bd1e995) % total weight`, chest loot `Scramble(seed ^ 0xC4E57)`,
  loose loot from the folded dungeon seed.
- One table system (A-80): `SpawnTables/Dungeon` rows key on content, socket kind, dungeon-type bits, biome,
  size and weight; `DungeonLoot` holds loose (kind -1) and chest (kind 1) loot. Row order is part of the seed.
- `DungeonContent` is append-only and never renumbered. Creature contents map to species by name.
- Checks already logged per dungeon: the pick hash twice (equal), and the same sockets as every other type
  (different), so a pick depends on the socket alone and the tables differ by type.

## 3. Who makes what, and what is sent (as built)

| Thing | Made by | Sent |
|---|---|---|
| Sockets, picks, names, marked chest | every machine, from the seed | nothing |
| Creatures | where animals live (solo, the host's server world): `FaunaDirector.Spawn` plus a `DungeonPost` | creature ghosts as today; the post stays local |
| Props (crates, urns, altars...) and their KitBox collision | every machine, presentation world | nothing |
| Loose loot | host or solo, `Items.Spawn` | the items' existing messages |
| Temple chests | host lays out 1-3 items from the socket seed | `OpenChestRpc` / `ChestOpenedRpc` |

Behaviours built: Drink at NearWater and Guard at NearChest (DungeonPostSystem, Burst, nudges brain modes).
r5 adds, in that order after chest and water: Roost (bat at a Ceiling socket hangs there whenever its brain
goes to roost), Patrol (walker on Passage/Doorway walks to the nearest passage socket 4-20 m off and back), Sleep
(walker, not ghost, at Dark naps 40 s at the socket, up 30 s). Posts are built from the seed, so nothing new is sent.
DungeonState gives quests each dungeon's name, entrance, creatures left, Cleared and marked chest.

## 4. Gap: dungeon creatures are not part of the one population (A-110)

Today every dungeon on the island is filled at load, and the post keeps the director from ever letting a
creature go. So the host carries every dungeon creature on the island for the whole session, outside the
director's tiers and caps, and a director rebuild makes the dead ones again.

Proposal, following DESIGN-population.md:
- The picks are the dungeon's residents: a record every machine already has. A dungeon's creatures become
  entities only while some player is within simulation reach of the dungeon (its socket bounds plus the
  director's spawn radius, fixed so every machine agrees). Visual reach never decides it.
- Spawning goes through the director's queue on a new `Dungeon` tier with its own cap; nearest dungeons win over
  the cap. A creature is let go at its post when every player has left; on return it is made again at its post.
- Dead or driven-off creatures are an exception record on the host: per dungeon, one bit per creature socket.
  A dead resident is never made again this session; a rebuild reads the same record. A plain record a save or
  world file could carry (rule 22).
- DungeonState reads the records and exceptions, not live entities, so CreaturesLeft and Cleared stop depending
  on what happens to be spawned.
- Fauna_Fingerprint may move; I report old and new and re-pin. The host cost is a BENCH-QUEUE job (rule 21).

## 5. Gap: multiplayer for the exceptions

- Solo and the host need nothing new. A joined client today cannot know Cleared or which residents are dead.
- Proposed message (rule 11 ask, mirrors ruling 2 of the population note): one per dungeon with any exception,
  dungeon id (uint) plus a ulong of dead creature sockets, 12 bytes; sent to a client as it joins and to everyone
  when a dungeon's bits change. Creature sockets per dungeon stay under 64 (caves at most 18, temple 15); more is
  a build-time error. No ghost field.
- Without the message, row 2 still works; only a client's Cleared stays unknown.

## 6. Gap: LOD by screen size (rule 20)

- Creatures already draw through CreatureRenderSystem's ScreenSize tiers.
- Props are one GameObject each (about 600 island-wide), drawn at any distance the frustum allows. Proposal: draw
  props instanced per (content, size) mesh set, with a tier from `DetailPolicy.Tier`: full parts, then a merged
  single mesh, then nothing below a few pixels. Collision boxes stay as they are (simulation, not drawing).
- Details tenet, optional: a cave mouth with a lit sconce or banner socket near it shows a far glint, picked by
  screen size like any far detail. Needs Zach's OK only if it reads as haze.

## 7. What the map editor would need (rule 22)

Sockets come from room shapes and picks from the seed, so an unedited world needs no file. Editing would add
plain records next to the world file, applied after `PickRow`, so nothing else moves:
- Socket override: dungeon id, socket key, content by name (not enum number), or "empty".
- Added socket: dungeon id plus a full DungeonSocket, appended after the generated ones so indexes hold.
- Dungeon table override: dungeon id to a SpawnTable asset name (a boss cave, a themed crypt).
- Socket key: index alone breaks when a set piece moves sockets (P-46 does). Key by kind plus position to 25 cm,
  falling back to index; an override that matches nothing is logged as orphaned, never fatal (as map-editor.md 4).
- Exceptions (section 4) are save data, not world-file data.

## 8. Proposed rows

| Row | What | Owner | Wire |
|---|---|---|---|
| 2 | Section 4: dungeon residents by nearness, Dungeon tier, host exceptions, DungeonState from records | animal | none |
| 3 | Section 5: the per-dungeon exceptions message, client Cleared | animal | rule 11 ask |
| 4 | Section 6: instanced props with ScreenSize tiers | animal | none |
| 5 | Behaviours from the brief still missing: roost (Ceiling), sleep (Dark), patrol (Passage, Doorway) | animal | none |
| 6 | Cave and ruin chests open like temple chests (`OpenChestRpc` needs a dungeon id) | items owner | rule 11 ask |
| later | Section 7 records, with M-07 | map editor | none |

Not touched by any row: TempleSockets and CaveSockets (set-piece owners), the DungeonSocket contract, Landmarks_V3.
