# Ships you can board (design note, P-44 row 1, #255)

This is a design note only. No code follows from it until the supervisor and Zach have reviewed it. It covers how a
player boards a galleon and walks its deck while it sails, built on what the colossus carry (A-107) already does.
Small boats and rafts use the same system later (rule 33). There is no second moving-platform system.

## What exists today

- **Colossus carry** (`ColossusClimbSystem.Ride`, `ColossusCollision.Carry`). Each frame, a player who stands on or
  holds a colossus column is moved by however far the body's root moved and turned since the columns were last
  gathered. A kick limit (`ColossusWalker.PosedClock` speeds) shakes off a climber at a lurch.
- **Clock-driven motion** (`ColossusWalker`). A walker's place comes from a seeded loop and the shared clock
  (`_PunchyClock`, the server tick; `Time.time` solo). The host and every client pose it alike, so **nothing is sent**.
  Who is riding is worked out on each machine from player places it already has (`ridden` bits).
- **Kit box collision** (`KitBox`, `KitCollision`, W-67). Rotated boxes from StructureKit parts. Tops are floors,
  sides are walls, bottoms are ceilings (tilted boxes give stairs and ramps). Player movement already reads them for
  buildings (`TopUnder` with kit boxes, `KitCollision.Wall` / `KeepOut` / `Ceiling`).
- **Climb columns** (`ColossusClimbColumn`: base, radius, height, grade, slot). Movement climbs a graded column
  like a trunk and stands on its top.
- **Riding a mount** (`PlayerRide`, `RideOff`, P-43 `RideOff.Size`). That is a seat, not a floor; ships do not use it.
- **Headroom** (`CaveBaking.RiderHead` 2.6 m, W-172). Use the same clearance under a deck.

## How a player stands on a moving hull

Reuse the carry and the kit boxes, and add one small piece: **a carrier is anything with a root pose at the shared
clock**. Colossi are carriers today. A ship becomes one more.

1. **The hull is kit boxes in ship space.** It is built from StructureKit parts (W-175 units once that format lands;
   a fixed part list until then): a deck floor, rails as low walls, a raised stern deck with a stair, masts. The boxes
   stay in ship-local space. Each frame, the boxes near a player are placed at the ship's current pose
   (`KitCollision.Placed` already turns, scales and moves a box) into the same kit-box buffer movement reads. Floors,
   rails and stairs then work with no new collision code.
2. **The carry generalises from slot to carrier.** `ColossusCollision.Carry(slot, point, out moved, out turn)` reads
   one root's pose now against its pose when last gathered. A ship's root is the same: pose at the clock now against
   last frame's pose. The smallest change is a `Carriers` lookup that `Ride` asks instead of `ColossusCollision`
   directly. Colossi register their roots and ships register theirs. Slots 0-63 stay colossi; ships take 64 and up
   (the `ridden` mask grows to two ulongs, or ships keep their own mask).
3. **Standing on a ship** is found the way the colossus top is found: the floor under the feet came from a ship's box
   last frame (movement records which carrier gave the floor, as `OnProp` records a prop today). Then `Ride` moves the
   player by the ship's delta and turns their facing by its turn. A player in the air is not carried. The ship sails
   away under a jump, so jumps stay short and on-deck jumps land a little aft at sailing pace (see Cost).
4. **Masts are climb columns.** A mast registers as a `ColossusClimbColumn` with a grade (rope-wrapped = easy). The
   rigging then climbs like a colossus with no new code. It is listed under Later because of the list size and look.

## What goes over the wire

| Step | Sent | New ghost field or enum? |
|---|---|---|
| Fixed sailing loop (row 2) | Nothing. Pose = seeded loop + shared clock, as walkers. | No |
| Who is aboard | Nothing. Each machine works it out from player places (as `ridden`). | No |
| Players on deck | Their usual NetPlayer pose (world space), already sent. | No |
| Gangplank down or up | Nothing: it is down during the loop's dock pause, by the clock. | No |
| A player steers (row 3) | The ship's place, heading and speed from the host; the steerer's input to the host. | **Yes: ask first (rule 11)** |

The one known drift is a joined player seen on deck. Their pose arrives interpolated, about one snapshot late, so at
sailing pace (2-3 m/s) they would be drawn 0.2-0.3 m aft. The fix needs no wire change: on each viewer, a remote
avatar who is aboard is drawn by taking their sent place into ship space at the snapshot's tick and back out at the
ship's pose now. Row 2's check measures it (a `-shippeers` probe like `-ccpeers`).

## What "simple" is (row 2)

- One galleon per dock town (or one on the map to start), placeholder hull from kit parts.
- A fixed loop off the dock: sail out, round, back, a dock pause with the gangplank down. Seeded and checked to stay in
  water deep enough for the hull, as walker loops are checked to stay dry.
- One open deck with rails, a stern deck with a stair, a wheel prop (no steering yet), masts and sails as visuals.
- Board by the gangplank at the pause, or swim up and climb a rope ladder (a graded column at the side).
- Walk the deck, jump off into the water. Animals keep clear of the hull (`CreatureBrainSystem.ColossusZones` takes
  the ship's circle).
- Drawn as one root with kit meshes, LOD by screen size (rule 20).

## Cost per step, and where it gets hard

| Step | Size | Where it gets hard |
|---|---|---|
| Carrier lookup over colossi + ships | Small (one interface, `Ride` asks it) | Touches fauna's `ColossusClimbSystem`: fauna reviews it. |
| Hull boxes placed at the ship pose each frame | Small (reuses `KitCollision.Placed` + the buffer) | Boxes must be placed after the ship moves and before movement, or the deck lags a frame. |
| Seeded loop in water, dock pause, gangplank | Medium | Finding a loop that clears piers, shoals and the beach. Reuse the walker loop search on water. |
| Hull from kit parts | Medium | Until W-175, a hand part list; later, a W-175 unit. |
| Remote players drawn on deck without drift | Small to medium | The snapshot-tick pose; needs the probe to prove it. |
| Jumping off or on while it sails | Small for "not carried in the air" | Jump-on from the dock while it moves: no momentum hand-over, so a moving deck slides under you. Fine at 2-3 m/s. |
| Steering (row 3) | Medium-large | Host-owned ship state, so a ghost or RPC: a rule 11 ask. Small boats share it. |
| Rocking in swell | Medium | Boxes tilt (fine), but the carry turns only about up today. It needs full rotation, and the ground normal tilts, so `SlideSlope` sends players sliding. Cap the roll at a few degrees. |
| Climbing the rigging | Small code, medium look | The colossus column list has a fixed size (`FixedList4096Bytes`, about 170 columns of 24 bytes near players); a ship's ropes must fit. |
| Below deck | Large | Ceilings exist (`KitCollision.Ceiling`), but the camera inside a moving hull, lighting, and hiding the inside from outside are the interiors work (docs/agents/interiors.md). |
| Crew folk, cannons | Large | Folk standing on a carrier needs the same carry in the folk movement. |

## Open questions for review

- Is one galleon on the map enough for row 2, or one per dock town?
- Is "carried on deck, not in the air" OK for a first version, or should jumps keep the ship's speed?
- For row 3: a host-owned ship ghost (new ghost type) or a steered-mount style RPC? Either one is a rule 11 ask.
