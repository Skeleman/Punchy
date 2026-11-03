# Rework brief: geography (water) and structure collision

For Zach. Read-only research on world-features (W-65g), 2026-11-03. Issues: #83, #88, #87, #36, #38, #24, #20.

## 1. Geography today

**How the ground is made (bake order, `TerrainAuthoring.BuildPlaces`):**
1. Base shape: biome cells or the v2 biome map give a height (`TerrainSource.ShapeHeight`).
2. Rivers (`Rivers/RiverBaking.cs`): a drainage sim on a coarse 16 m grid. It floods from the sea, finds lakes,
   sets river beds and water levels, then runs repair passes: `LowerBreachedLakes`, `ReshapeLoweredLakes`,
   `FillRiverPits`, `CutDams`. Two river maps are baked: a jittered one for placing things, a smooth one to draw.
3. Landmarks, settlements, roads, the salt tower, caves, fishing villages, turtle and sea folk: each placed
   against the jittered rivers, mostly with ponds and rough banks switched off.
4. Then the drawn rivers swap in, rough banks and ponds switch on, and the pond mask drops small ponds.
5. Overlays on top: grand falls (fixed coordinates, seed 8121 only), archipelago, icebergs.

**How water is decided (`TerrainSource.SampleGround`):** there is no list of water bodies. Each point asks
every source in turn (sea, river, lake, lake bay, landmark pool, grand falls, stepped pool) and keeps the
highest surface. Each source also reshapes the ground on its own, one after another.

**Root causes of the water bugs:**
- **Water is a per-point "highest wins" of seven independent sources.** No source knows the others. Where two
  meet (a lake's wide cells under an upper river, a landmark pool by a river, a paddy by a lake) you get land
  between surfaces, walls of water, or water over dry pits. Fixes so far are special cases: `FallWater`,
  the bay rule, the "lake deeper than itself" cut.
- **The river grid is too coarse for the shapes it draws.** Cells are 16 m, channels up to ~50 m wide, with
  meander drawn on top. A cell's level covers ground it never measured. Steep drops between cells give
  "floating water" (W-43: 23 left; 3 under the cascade rule).
- **Ground is carved after water levels are fixed, then changed again.** Landmark stamps, rough banks, ponds
  and settlement levelling all run after the river levels are set, so water can end up above its banks.
  Flatter house plots (#87) would be one more layer of this kind, so they belong in the same rework.
- **Places sit on a different world than the one drawn.** Landmarks are chosen on jittered rivers without
  ponds or rough banks. This was done on purpose to keep hashes stable, and it is why every fix is a late patch.
- **Stepped pools are not part of the water system.** `Terrace` sets each paddy at the natural ground height
  of its centre, from noise. It doesn't know about rivers, lakes or the real slope. #83 calls them "very
  incomplete". The 16 m pond mask then drops pools that its banks split.
- **Waterfalls are a side effect.** A fall is any bed step of `FallDrop` or more, so falls appear where the sim
  happened to step (#88's accidental cliff by the sea folk lake). Grand falls are hand-surveyed for one seed.

Past water issues show the pattern: #3 (cave streams), #18 (river curves), #19 (swamp pools), #42 (falls),
plus reports W-21 (fall gap), W-35 (stale lake level, water 4.8 m over ground), W-36 (river pits).
Each was fixed on its own. #83 asks to stop patching.

## 2. Collision today

**Three separate systems, none of them the mesh itself:**
- **Upright circles** (`FloraGenerator.KitStructure`): each part marked `Solid` becomes a circle of radius
  0.55 x its longest side, up to the eaves. A 4 m wall becomes a 2.2 m circle, so walls are set back by hand
  (W-58b) and wharf houses need their own case (W-65). The player is pushed out sideways only.
- **Support grid** (`FloraMeshBuilder.SupportGrid`, `PropSupport`): a heightfield sampled from the mesh, up to
  3 layers. Cell size is the model's size / 40 (at most 48 cells a side), so a 30 m wharf gets ~0.75 m cells.
  It gives what you stand on, and a "wall" wherever a top is too high to step onto. It only exists near a player.
- **Special cases in the terrain:** cave distance field, `TempleAir`, `TowerWall`, `Deck` (bridges, berg steps).

**How the player uses them (`PlayerMovementSystem`):** push out of circles, then walls from the grid, then
pick the floor: a prop top counts only if it is higher than the ground and within the 0.4 m step-up.

**Why every new structure fails:**
- **Thin parts fall between grid samples.** Igloo door slab 0.14 m (fixed with a hidden 1 m snow block), pier
  plank gaps (fixed with a solid bed under the planks), stepping-stone tops ~0.36 m wide in a grid with holes
  between them (W-65f/g: the walker still drops in at the row end).
- **Grid cells grow with the model.** The 4.6 m dock ramp rose 0.5 m per cell, too high to step (W-62b): fixed
  by making ramps 8 m long, not by fixing the grid.
- **Nothing blocks the space under a deck.** If the first tread is more than 0.4 m over the bank, the ground
  wins and the player walks under the wharf between the piles (W-65c to W-65g, still failing). Solid treads
  became circles, which the walker goes round.
- **Solid is opt-in per part.** New pieces (treads, stones, door slabs, ruin rubble) start non-solid; someone
  notices only after a walk.
- **Walk checks can't see it.** The editor BFS sim and the player `-walk` shot use different code, and the sim
  only sees grids near the player. `-walk` reports ARRIVED by x/z only: W-65g "arrived" while wading under the
  deck. Ruins (W-56) gave a false sink because the probe picked the wrong stacked room.

## 3. Options: geography

**G1. Keep the pipeline, add a water audit gate.** A probe that counts water over ground, open ends, land
between surfaces and dry paddies, run on every world card. Fix the worst spots in place.
Effort small. Hashes stay. Multiplayer unchanged. But it keeps patching, which #83 rejects.

**G2. Water-first rebuild on a new island version (v3, behind a flag like v1/v2).** Bake one hydrology table
first: lakes, river segments, pools, stepped-pool chains and falls as real bodies with their own surface. Then
carve the ground to fit the water (water decides ground, not the reverse). Finer river grid (4-8 m). Falls and
shelves (#88) become authored features of the table. Then place landmarks on that final world.
Effort large (several batches). v3 gets new hashes and every landmark moves once; v2 stays for comparison.
Multiplayer: still seed-derived, but host and client must run the same island, which is not synced today.

**G3. Unify water inside v2.** Same water table, but keep current placement (jittered rivers).
Effort medium-large. Promises landmark hashes stay, but the placement world keeps differing from the drawn
one, so new mismatches will keep coming.

**Recommendation: G2.** Do G1's audit first (it is needed anyway to prove v3 is better), then v3. Sync the
island version from host to client before v3 ships.

## 4. Options: collision

**C1. Harden what exists.** Each kit part declares collision (block / walk-on / none) instead of opt-in Solid;
grid cells fixed at ~0.2 m per part; a solid "skirt" under every deck down to the ground.
Effort medium. No world hash change. Still two systems that can disagree.

**C2. Box colliders straight from the kit parts.** Every `KitPart` is already a box with a rotation. Use
those boxes (plus ramps) as the collision for kit buildings, wharves, piers, stairs, stones, igloos and ruins:
stand on tops, blocked by sides, headroom from bottoms. Collision then always matches the art. Flora keeps
cylinders. Add one walk-test harness for every new kit shape that checks feet height, not just x/z.
Effort medium-large. No world hash change (colliders are not hashed). Deterministic and seed-derived, so host
and client agree. Needs `PlayerMovementSystem`, which is lent to the animal lane: plan a slot.

**C3. Move to a physics engine (Unity Physics or PhysX) with mesh colliders.** The project uses no physics
today. Terrain streams, caves are a distance field, climbing is custom. Effort very large; would change how
the player moves and climbs. Not recommended.

**Recommendation: C2**, starting with wharves, stepping stones and igloos (the open #36/#38 failures).

## 5. Open questions for Zach

1. Can v2's landmark positions move once (G2/v3), or must the current island stay frozen?
2. Do you want v3 as a separate island to compare, or replace v2 when ready?
3. Should waterfalls and stepped pools be authored features (chosen spots, set shapes) instead of whatever
   the drainage sim produces?
4. OK to borrow `PlayerMovementSystem` from the animal lane for C2?
5. Should the island version be synced over the network now, before any v3 work?
6. Which comes first: water (#83, #88) or collision (#36, #38)? They don't touch the same files.
