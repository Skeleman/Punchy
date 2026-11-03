# Building interiors (design note, W-32)

This is a design note only. No code follows from it yet. It covers how village huts, houses and bigger buildings
could get insides people walk into. It builds on what the sunken temple (W-32, `SunkenTempleShape`) does today.

## What the temple already proves

- **Analytic rooms.** Rooms are axis boxes in the landmark's own frame (u, y, v), and `Solid` / `Floor` / `Wall`
  are pure functions. `TerrainSource.Deck`, `TowerWall` and `FloorUnder` ask the landmark first, so the player,
  folk and anything else that uses `FloorUnder` get collision that matches the drawn mesh with no physics
  colliders. The cost is a cell lookup plus a handful of box tests, and only near the building.
- **Hidden until you're close.** The interior mesh, sconce lights and inside props draw only at LOD0 and only while
  the camera is inside the building's box. Otherwise a cheap plug mesh closes the door and shaft holes. Interior
  tris (about 2.7k) cost nothing from outside.
- **Seed-derived.** The layout comes from the landmark instance (position, facing, base), so every peer builds the
  same rooms with no network traffic.

## Enterable vs facade

| Kind | Examples | Inside |
|---|---|---|
| Facade | Most village houses seen from the road, far towns | Closed walls; windows are dark or emissive cards |
| Enterable small | Huts, houses, the smithy, shrines | 1-3 rooms, ground floor, one door |
| Enterable large | Temple, keep, tavern, caves under towns | Several rooms on levels, stairs, culling by room |

Rule of thumb: a building is enterable only if a folk job or a quest happens inside it. Everything else stays a
facade. A setting can switch a facade to "openable" later, because the shell is the same mesh.

## A shared room kit

- One data type `RoomSet`: a list of room boxes (u0, u1, v0, v1, floor, ceil), doors (room a, room b, wall side,
  span), stairs (tread list), solid props (box list) and lights. This is what the temple's tables already are, just
  pulled out of `SunkenTempleShape` into a blob.
- Kit pieces drawn from that data: floor, ceiling, wall-with-openings, door frame, stair, window, sconce, hearth.
  The W-44 kit builders (wall runs, roofs) build the shell. The room kit fills the inside.
- Building types (asset) pick a `RoomSet` template plus a jitter seed. Huts: one room plus a hearth. Houses: 2-3
  rooms. The temple stays hand-tabled.

## Door and portal culling

- Each room knows its doors. At LOD0, flood from the camera's room through open doors within a depth of 2-3 and draw
  only those rooms. Outside, a building draws its shell plus the room straight behind an open door, so you can
  see in.
- Room meshes are built per room (not per building), so culling is just skipping draw calls. Shadows inside stay
  off; the shell casts them.

## Lighting

- Inside light is local: emissive sconces and hearths plus at most 4-8 real point lights per building, turned on
  only while the camera is inside (the temple does this). URP Forward+ is needed if more than about 8 lights can
  be live at once.
- Daylight comes in through openings: roof shafts or windows with a fake light cone (additive mesh) instead of
  real shadows.
- Ambient: interiors want a darker ambient than outside. Use a per-room ambient tint through a material property.
  Don't change the global ambient.

## Folk walking inside (rule 9 doors)

- The animal lane's rule 9 doors give folk a door point and a facing. The room kit exposes the same door records,
  so a resident's path is door -> room centre -> job spot (hearth, bed, bench), all on `FloorUnder`.
- Folk inside an unseen building are simulated but not drawn. Their pose updates are cut to a low rate.
- Doors are open frames by default (no door state). Swinging doors are shared world state (see multiplayer).

## Multiplayer

- Layouts are seed-derived: nothing is sent.
- Door open/closed, broken walls and taken items are shared world state, owned by the host and keyed by
  (landmark or building id, door index).
- Interior visibility is local to each player's camera. Two players in different rooms each draw their own rooms.

## Performance

- Collision: an analytic box test per query near a building. That's cheaper than mesh colliders, with no physics
  world.
- Rendering: interiors at LOD0 only, room-level culling, no interior shadows. Budget about 3k tris per enterable
  house and 10k for a large building inside view.
- Memory: room meshes are built lazily on the first approach and dropped beyond LOD0 range plus some margin.

## Recommended first step

Pull the temple's room tables into a `RoomSet` blob with shared `Solid` / `Floor` / `Wall` code. Then make **one
village hut type enterable**: one room, one door, a hearth light, with folk using its rule 9 door to go in and out.
That tests the kit, the door hooks and the culling on the cheapest building before houses and keeps follow.
