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
  only. I provide **Anchors** for its creatures (see below); we agree that interface directly.
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
   - Style tags carried on cave Anchors for the animal session (which fauna live where).
3. **Cave interiors.**
   - Stalactites and stalagmites in larger rooms (ceiling and floor props, density by room size and style).
   - Underground rivers along passage floors and pools in chambers (cave water surfaces, flow along the passage).
   - Glowing flora on cave floors and walls (glow fungi, crystal growths, ice blooms by style).
   - Anchors: Roost on chamber ceilings, Nest and Perch on ledges, Burrow on soft floors, Pool at water.
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
   - Anchors: Nest and Perch on roofs, chimneys and towers; Hearth and Yard for domestic animals (hearths, doorsteps,
     yards); Door, Work, Stall, Well, Bench, Gate, Altar and Seat for residents.
6. **Imp warren (cave settlement).**
   - A pale, imp-like folk living in a cave network, with crude architecture of their own (stacked-stone huts, rope
     bridges, lean-tos against cave walls) and milky white pools nearby.
   - Reuses `SettlementLayout` and the resident system, inside a cave network instead of on the surface.
   - The imp species asset and any creature AI they need come from the animal session; I provide the warren, its
     layout and its anchors.
7. **Later:** settlement growth along roads, outlying farms, ruins.

## Anchors (agreed with the animal session)

The only contract between this branch's geometry and creature behaviour (see GLOSSARY.md). Baked from the seed, so
nothing to sync; immutable (the animal session counts occupancy from its creature snapshot).

- `Anchor { float3 Position; float2 Facing; AnchorKind Kind; byte Tags; int Owner; byte Capacity; float Size; }`
  - Position is absolute, on the surface the creature uses: the ceiling point for a Roost, the ridge or sill top
    for a Perch.
  - Capacity: creatures it holds (0 = unlimited). Size: radius of the spot (m); creatures spread inside it.
  - Owner: the settlement or cave network. Tags: cave style or settlement folk, biome, and a Shelter bit.
- `AnchorKind` (a byte, append only): Door, Work, Stall, Well, Bench, Gate, Altar, Seat, Roost, Perch, Nest, Burrow,
  Pool, Hearth, Yard. The list goes into GLOSSARY.md in the same commit as the enum.
- On `TerrainSource` as a blob with a Cell grid: `Anchors.Near(position, radius, kind)` and an allocation-free,
  Burst-safe `Anchors.Pick(ref blob, near, radius, kind, ref rng, out anchor)` (uniform reservoir pick, like
  `FloraPlacement.FindTree`), which the brain calls while Deciding.
- First kinds land in phase 3 (Roost, Burrow, Pool, plus Nest and Perch on cave ledges); building kinds in phase 5.

## Log

- 2026-10-27: Worktree created from 218d10d, fast-forwarded to 5413848 (animal-behaviours). Plan written.
- 2026-10-27: Phase 1 done (e1613a4). Cave sampling A/B on the baked world (6 caves, 36 segments): identical within
  4 m of walls (19,600 samples, worst difference 0, no sign flips), old sweep 457 ms vs grid 284 ms per 200k managed
  samples. Chunked walls: 65 chunks (26 with walls), 78,922 triangles, no open edges below the rim. The first chunked
  version left 4 seam edges (a float origin plus an offset rounds differently in neighbouring chunks); fixed by
  sampling on a global integer grid.
- 2026-10-27: Phase 2 (cave sizes and styles), without the Glacier biome (its own commit, pending the user).
  - Caves are networks: segments join any two nodes, so side passages branch off the main passage and off each
    other; nodes are tagged Passage, Chamber or Shaft. `LandmarkType` gains CaveChambers, CaveBranches,
    BranchLength, CaveShafts and CaveStyles. Sizes: **Grotto** (one chamber, ~20 m), **CaveEntrance** (cavern: an
    extra chamber, two side passages, ~100 m), **CaveNetwork** (three extra chambers, seven side passages, two shafts,
    ~380 m).
  - `CaveStyle` assets (Landmarks/CaveStyles): passage shape (width, height, roughness, grain) is baked; the look
    (rock colours and banding, gloss, metallic, crystal facets, ice translucency, glow in cell-border cracks plus a
    share everywhere) is a table CaveSystem uploads each frame, so looks tune live. Styles: Limestone (Temperate,
    Grassland), Granite (Mountains: tall fissures), Sandstone (Sonoran, Dunes, SaltFlat: low banded galleries),
    LavaTube (Ashlands, Volcano: smooth black tubes, glowing cracks), Fungal (FoggyMarsh, PoisonSwamp: wet, green
    glow), Crystal and Metallic (SparklingForest), Ice (Tundra for now; Glacier once it exists), Rock (fallback).
  - Biomes list all three sizes (grotto 0.6, cavern 0.3 where newly added, network 0.12 per km²). This seed: 17
    caves (10 grottos, 6 caverns, 1 network of 384 m with 11 chambers), in five styles.
  - Checks on the baked world: sampling with the grid vs a sweep of every segment, 27,920 samples near walls, no sign
    flips, worst difference 3.4 cm (stretched profiles); 329 vs 561 ms per 200k managed samples. Chunked walls: 184
    chunks, 322,608 triangles, no open edges below the rim, 4 duplicate triangles (degenerate cells, not seams).
    Looked at in game: LavaTube, Ice, Metallic, Sandstone.
- 2026-10-27: **Glacier** biome (confirmed by the user: high, cold country). Appended last to the terrain's land
  biomes (World.unity), so earlier biomes keep their share of each region's roll. Climate: altitude 0.72–1,
  temperature 0–0.12, humidity 0.3–1; a broad rounded ice sheet (base 120 m, relief 90 m) of snow with GlacierIce on
  slopes and in patches, rock on cliffs; no plants; Tundra's fauna for now (the animal session may retune); caves of
  all three sizes at high density, all in the Ice style. Reshuffle on this seed: 24 of 196 regions changed biome — 16
  became Glacier (11 Tundra, 3 SparklingForest, 2 Mountains) and 8 flipped elsewhere as the draw shifted (7
  Mountains → Tundra, 1 SparklingForest → FoggyMarsh). Glacier now holds an ice CaveNetwork (418 m, 11 chambers),
  a cavern and four grottos.
- 2026-10-27: User roadmap merged (via supervisor): building types sized to inhabitants, roads and signs (moved up
  into phase 4), landscaping and water, leader and religious buildings; cave sizes, per-biome styles (glacier ice
  caves, metallic and crystalline caves in the mystical forest), stalactites, underground rivers, glowing flora; an
  imp warren; creature hook points for the animal session.
