# Dungeon pieces (P-45, #24 phase one)

What a "dungeon piece" is on main today, and how caves, ruins and new rooms build on it. Phase two (filling the
sockets) is the animal lane's DungeonSpawner and is not covered here.

## The pieces are the rooms we already have

There is no separate piece format. A piece is a room that already exists in one of two places:

| Piece | Where it comes from | Typical sockets |
|---|---|---|
| Hall | Sunken temple room with `TempleSockets.IsHall` (long and narrow) | Creature along it, Clutter at the sides, WallDecor |
| Dead end | Sunken temple room with `TempleSockets.IsDeadEnd` (closed, one doorway) | Chest at the far end (NearChest) |
| Chamber | Any other temple room, or a cave chamber | Creature (Large in big rooms, Ceiling), Clutter, WallDecor |
| Pool room | Cave Lake room (`CaveRoomKit`, W-115 row 2) | the cave spots plus NearWater |
| Grotto, roost, buried ruin | The other `CaveRoomKit` rooms | the cave spots, tagged with biome and chamber type |

The hall and dead-end rules live once in `TempleSockets` (`Touch`, `IsHall`, `IsDeadEnd`). The socket builder and
`-place piece:hall|deadend` both call them, so a shot and the sockets always agree on what a room is. Every room
type gives `DungeonInfo` + `DungeonSocket` entities. `SocketDebugDraw` (F9 / `-sockets`) draws all of them, temple
and caves alike.

## How caves and ruins reuse them

- **Cave chambers (W-115):** the carved passage meets a `CaveRoomKit` room at its doorway. The room's floor and walls
  come from the carved cave field and `TerrainSource.FloorUnder`. The kit adds only props, lights and solids
  (`AddKitBoxes`). Sockets use the same `SocketKind`s as the temple, plus the biome and chamber type in their tags so
  spawner tables can be written per chamber.
- **Buried ruin room:** `CaveRoomKit`'s Ruin room reuses the ruin kit's tiles inside a cave. It is the bridge between
  the two families: the same tiles above ground (sunken temple) and below (cave).
- **Sunken temple:** its `RuinRooms` layout is the reference set of halls, dead ends and chambers. The floor comes
  from `FloorUnder`'s temple branch.

All three stand on `FloorUnder`, so walking, creatures, the `-dungeonwalk` check and networking need nothing new
per piece (rule 33: one ground source).

## What waits

- **New underground rooms** (a free-standing hall or crypt that isn't a temple room or a cave chamber) need a floor
  that `FloorUnder` knows about. They wait for **W-174** (carved-field ground edits: the room carves the field, so
  `FloorUnder` sees it) and **W-175** parts (the walls and props as shared parts, the room layout as data). Adding a
  second floor source to `FloorUnder` for them now would break rule 33.
- **M-08 set-piece view** shows kit set pieces but not temple pieces, because the temple mesher is private to the
  terrain code. Showing temple halls there comes later, once W-175 makes the walls shared parts. Don't make the
  mesher public just for the viewer.

## Checks

- `-place piece:hall`, `piece:deadend`, `piece:pool` stand you in one of each, and log the counts by kind. The
  piece is placed again when the island's landmarks change (the editor builds the baked island first, then moves the
  temple). For a hall, the camera looks down its length.
- `-dungeonwalk N` walks the first N caves, ruins and temples and reports falls, in-rock and stuck spots.
- `-sockets` draws every socket. A filled run (DungeonSpawner) shows what lands in them.
