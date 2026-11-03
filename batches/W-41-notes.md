# W-41 notes: rock set pieces for cliffs (read-only study, 2026-11-02)

Question (Zach): would large rock set pieces make cliffs that behave more flexibly than a heightmap?
Read from `world-features` (terrain, flora, landmarks, icebergs) and `animal-behaviours` (PlayerMovementSystem).

## 1. How terrain is made now

- **Ground = one height per (x, z).** `TerrainSource.SampleHeight` sums the biome shape noise, the island v2 lift
  grid (`IslandV2`: 512 x 512 cells over 9216 m, about 18 m per cell, bilinear), rivers, landmark stamps (dome,
  mesa, Cliff = a step with a steep face), roads and the overlays. `BuildChunkMeshJob` meshes 64 m chunks, 64 quads
  per edge at LOD 0 (1 m quads), 5 LODs, view radius 24 chunks.
- **Caves are the only true 3D ground:** a signed distance (`CaveDistance`, `IsRock`, `FloorUnder`) carved under
  the surface, walled with surface nets in `CaveSystem`. Icebergs and salt towers are analytic shapes that answer
  `TowerWall` (blocked?) and `Deck` (floor under a point) in `TerrainSource`.
- **What a heightmap can't do:** no overhangs, arches or undercuts; a sheer face is at best a very steep ramp
  between two 1 m vertices (and the 18 m lift grid rounds big walls further); the face gets few vertices, so
  top-down UVs stretch. The terrain shader already blends in the caves' rock grain "read three ways" on steep
  ground (normal.y < 0.55-0.8), but only within 40-90 m, and strata only where a surface asks for them.
  W-40 #4 fixes the grass/snow-on-cliff order bug, which is most of what Zach saw.

## 2. What rock set pieces would need

- **Meshes and materials exist in spirit, not as assets.** There are no imported rock meshes. Rocks are the
  procedural Boulder form (`FloraGenerator`: lump, slab, L, standing, monstrous; 8 seeded variants x 3 LODs, Flora
  shader, `Boulder`/`RedBoulder` assets at 1-2.6 m, `MaxSlope` 32). A cliff piece would be a new large variant
  family (10-40 m slabs and columns, strata/rock-grain colour from the terrain's rock look), generated the same way.
- **Placement:** a seeded pass on its own stream (seed ^ a new constant, Scramble'd), baked after every place like
  Icebergs/Archipelago, so landmark, cave and road hashes never see it. It picks spots where the ground is steeper
  than the cliff threshold along divider ranges, reading (never writing) landmarks, roads/plots, rivers, cave
  mouths and passes to stay clear. Pieces must not change `SampleHeight`, or flora, anchors and later bakes move.
  Flora under a piece needs a cheap "inside a set piece" reject in `FloraPlacement` (shared, additive).
- **Player collision today:** (a) `FloraObstacle` cylinders within 24 m, rebuilt by FloraSystem, slid around in
  2D (rocks give a few upright cylinders each); (b) `TowerWall` + `Deck` from TerrainSource for towers/icebergs,
  which give walls and walkable tops; (c) `PushOutOfSolids`/`SolidDistance` (3D spheres, overhangs, ceilings),
  but it only runs where `FloorUnder` says "in a cave". So: a piece that is a wall plus a walkable top can join
  `TowerWall`/`Deck` like icebergs did, with **no PlayerMovementSystem change**. Real overhangs the player can
  stand under need the piece's distance in `SolidDistance` and the push-out gate widened: that is
  PlayerMovementSystem (animal lane owns it). Creatures use none of these today (they walk through towers and
  rocks), so creature avoidance is optional, later, animal lane.
- **Streaming/LOD:** drawn through FloraSystem's tile layers (`RenderMeshIndirect` per model x LOD); Trees layer
  reaches 1500 m, about the terrain view radius, so ranges read from afar. Collision queries need a small spatial
  grid in the blob (like landmark cells), not a scan.
- **Network:** pure seed-derived scenery; host and client compute the same set. Nothing to sync.

## 3. Cost estimate

- **Draws:** one indirect draw per variant x LOD in view, plus shadows: 6 variants -> up to 18 + 18. Small next to
  flora. **Tris:** the far LOD must be very low (a 30 m slab seen at 1 km); budget about 2-4k tris LOD 0.
- **Memory:** meshes under 1 MB; instances about 64 B each, 10-20k pieces on all ranges = about 1-1.5 MB, plus
  the cell grid. Collision per player step: a few shape tests from one grid cell, comparable to icebergs.
- **Work size:** 3 world batches (pilot, all ranges + flora/road avoidance, polish/shots) plus 1 small animal
  lane batch only if overhang push-out or creature avoidance is wanted (about 1/4 of the total). Shared edits:
  `FloraGenerator`/`FloraPlacement`/`FloraSystem` (additive), `TerrainSource` entry points (`TowerWall`/`Deck`).

## 4. Recommendation: partial yes, after W-40

Finish W-40 first (organic ranges, slope shading after the lift, triplanar on tall faces); it removes most of
the "flat texture up a wall" look for free. Then pilot set pieces as cosmetic-plus-wall rock on **one** divider
range: a new large Boulder-family variant, its own seed stream, placed only on faces steeper than the cliff
threshold, collision through `TowerWall`/`Deck` (no PlayerMovementSystem edit, landmark hash unchanged).
Judge by shots and draw calls; only if it reads well, extend to all ranges and then ask the animal lane for
overhang push-out. Don't replace the heightmap or make cliffs from pieces alone.
