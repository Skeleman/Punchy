# World features: towns and cave systems

Plan, ownership and log for the `world-features` branch (worktree `D:\Unity\Punchy-world`, session claude-01).
Merges into `main` are Zach's call. Each phase is its own commit, so the profiling session can review them one at
a time.

## Ownership

- **Mine:** `Terrain/Caves/**`, `Terrain/Landmarks/**`, new `Terrain/Settlements/**` and `Terrain/Roads/**`,
  `Assets/Game/Landmarks/*.asset`, new settlement, cave style and road assets, `TerrainSource` cave, landmark and
  road entry points.
- **Shared, additive only:** `Flora/FloraGenerator.cs` (new forms in Grow's switch), `Flora/FloraPlacement.cs`
  (new placement rules in FloraRules/FloraPropEntry and Build), biome assets (Landmarks lists only).
- **Animal session ("Animal behavior manager", claude-41 [635c9b]):** `Fauna/**`, `CreatureBrainSystem` and all
  creature behaviour, including bats, glowing cave fauna, burrowers, settlement pets (dogs, cats, rooftop birds) and
  animal remnants (nests, feathers, footprints). Ask before brain changes; new `CreatureTask` values are appended
  only. I provide **hook points** for its creatures (see below); we agree that interface directly.
- **Profiling session (other PC):** `Weather/**`.
- **Not mine either:** `DebugControls`, `ThirdPersonCamera`, `NetMenu`, `CreatureGhosts`.
- Builds go to `Builds/World/`.

## Principles

- **Seed-derived, so nothing to sync.** Towns, streets, roads, plots, caves and their contents are baked or derived
  from the seed with integer hashes. No `UnityEngine.Random`, no `_Time`.
- **Chunk-friendly.** Everything sampled per point gets a coarse spatial grid in its blob, so cost stays flat as the
  world grows: a point only looks at the few items whose bounds reach it. Runtime building is per fixed-size chunk,
  with small per-frame budgets and results picked up a frame or two later.
- **Data-driven.** New content is assets: cave styles, settlement layouts, building types, road types, referenced
  from `LandmarkType` and biomes.
- **Sized to inhabitants.** Buildings, doors, ceilings and plots scale with the species that lives there (an ogre
  hall is not a goblin hut scaled up; it's laid out for an ogre).

## Phases

1. **Cave performance foundation.** *(done, e1613a4)*
   - Segment column grid in `CaveSetBlob`: a sample tests only nearby segments.
   - Cave walls built in 32 m chunks, streamed nearest first with a per-frame budget, seamless across chunks.
2. **Cave sizes and styles.**
   - Size classes from small grotto (one chamber off the entrance) through caverns to large multi-room networks:
     branching passages, loops, several chambers of different sizes, extra entrances (sinkholes, shafts).
   - `CaveStyle` asset: passage profile (round tube, tall fissure, wide flat gallery), wall noise, rock colours and
     finish (matte, wet, metallic, crystalline, icy), glow, ambient particles. Biomes pick styles; a landmark can
     force one.
   - Styles per biome: **ice caves** in a new **Glacier** biome (blue translucent walls, frozen falls), **metallic and
     crystalline** caves in the mystical forest (Sparkling Forest), lava tubes in ashlands, fungal caves in the marsh,
     banded sandstone in arid land, limestone elsewhere. More to invent as it goes.
   - Style tags exported per cave and per chamber for the animal session (which fauna live where).
3. **Cave interiors.**
   - Stalactites and stalagmites in larger rooms (ceiling and floor props, density by room size and style).
   - Underground rivers along passage floors and pools in chambers (cave water surfaces, flow along the passage).
   - Glowing flora on cave floors and walls (glow fungi, crystal growths, ice blooms by style).
   - Hook points: **roost spots** on chamber ceilings, nest ledges, water spots.
4. **Towns: layouts, streets and roads.**
   - `SettlementLayout` asset on `LandmarkType`: street pattern (square with radial lanes, main street, grid, ring
     wall with gates), plot sizes scaled to the residents' species, building mix by role.
   - `SettlementBaking` lays out streets and plots per settlement into a `SettlementBlob` with a grid. Streets paint a
     path surface and clear plants; plots get levelled pads.
   - **Roads between settlements** (and to cave entrances and landmarks): routed over the terrain avoiding steep
     ground and water, baked as spline segments in a grid, painted and levelled like streets, with bridges or fords
     where they must cross rivers. **Signposts** at junctions and town gates.
5. **Buildings, landscaping and water features.**
   - More building types per culture, sized to their inhabitants: houses (timber, stone, longhouse, burrow), inn,
     smithy, market, storehouse, workshop, stable.
   - A **leader's building** per culture: mayor's hall (humans), jarl's hall (dwarves), tribe leader's lodge
     (goblins, ogres), elder's spire (elves).
   - **Religious buildings** per culture: chapel or temple, shrine, standing-stone circle, totem ground.
   - **Landscaping and water**: fountains, wells, ponds, gardens and allotments, hedges, fences, lamp posts.
   - Hook points: **nest and perch spots** on roofs, chimneys and towers; **pet spots** (hearths, doorsteps,
     yards); door, workplace, market stall, well and bench points for residents.
6. **Imp warren (cave settlement).**
   - A pale, imp-like folk living in a cave network, with crude architecture of their own (stacked-stone huts, rope
     bridges, lean-tos against cave walls) and milky white pools nearby.
   - Reuses `SettlementLayout` and the resident system, inside a cave network instead of on the surface.
   - The imp species asset and any creature AI they need come from the animal session; I provide the warren, its
     layout and its hook points.
7. **Later:** settlement growth along roads, outlying farms, ruins.

## Hook points for creatures (to agree with the animal session)

Baked from the seed, so every machine has the same ones, and kept in spatial grids so creatures nearby find them
cheaply. A first proposal:

- `WorldHook { float3 Position; float2 Facing; HookKind Kind; byte Tags; int Owner; }`, where Kind is one of Roost
  (cave ceiling), Nest (ledge, eave), Perch (rooftop, chimney, tower top, signpost), PetSpot (hearth, doorstep,
  yard), Burrow (soft cave floor or bank), Water (pool or river edge), and Owner is the settlement or cave.
- Tags carry the cave style or settlement culture (and biome), so a species can filter for "ice cave roosts" or
  "human village roofs".
- A lookup like `WorldHooks.Near(position, radius, kind)` over a column grid, readable from Burst.

## Log

- 2026-10-27: Worktree created from 218d10d, fast-forwarded to 5413848 (animal-behaviours). Plan written.
- 2026-10-27: Phase 1 done (e1613a4). Cave sampling A/B on the baked world (6 caves, 36 segments): identical within
  4 m of walls (19,600 samples, worst difference 0, no sign flips), old sweep 457 ms vs grid 284 ms per 200k managed
  samples. Chunked walls: 65 chunks (26 with walls), 78,922 triangles, no open edges below the rim. The first chunked
  version left 4 seam edges (a float origin plus an offset rounds differently in neighbouring chunks); fixed by
  sampling on a global integer grid.
- 2026-10-27: User roadmap merged (via supervisor): building types sized to inhabitants, roads and signs (moved up
  into phase 4), landscaping and water, leader and religious buildings; cave sizes, per-biome styles (glacier ice
  caves, metallic and crystalline caves in the mystical forest), stalactites, underground rivers, glowing flora; an
  imp warren; creature hook points for the animal session.
