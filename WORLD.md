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
   - *(done)* Anchors (5ba72f1); the look rework from Zach's reviews: jagged ceilings, flat stepped floors, floor
     materials, frosted ice, obsidian lava tubes (033fc81, bdb4071); lava pools (575986b, walkable since e9df26b);
     speleothems built into the rock (c696ac1).
   - Rocks, rubble and mossy shrubs on cave floors, through the flora path (shared meshes, instanced).
   - Underground rivers along passage floors and water pools in chambers (cave water surfaces, flow along the
     passage); Pool anchors at water.
   - Glowing flora on cave floors and walls (glow fungi, crystal growths, ice blooms by style).
3b. **Water surfaces** (Water.shader, after underground water; tell the animal session first, as the shader predates
   both sessions).
   - Finer detail: small-scale ripples and normals (a scrolling detail normal or two, reusing the shared cave detail
     texture where it fits), varied by wind and by calm against rapids; better sparkle and highlights; gentle foam
     streaks on moving water.
   - Towards the shore, from the depth it already reads: clearer and more tinted in the shallows so the bed shows, a
     deep-to-shallow colour gradient, a foam or wet line with soft lapping, and darkened wet ground just above the
     waterline. Lakes, rivers, the sea and underground pools.
   - GPU-bound already on the profiler's PC: a few texture samples, no extra passes; benchmarked before and after.
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
   - **Coastal towns** (Zach, after roads and the warren, before world events): a settlement variant on sea or big-lake
     shores. Mine: shore placement, dock and pier forms (planks on posts over the water), a boathouse, net- and
     fish-drying racks, crates and barrels, Dock and Mooring anchors, and moored rowboats as static flora forms.
     Animal session: fishermen (Fish and Mend-nets tasks) and any moving boats (bobbing, multiplayer sync); agree the
     interface first. Reuse the shared building parts and the water shader.
8. **Late: world events** (after towns, roads and the warren; not started). Agree ownership first: sky and weather
   rendering and WeatherSystem's Sun belong to other sessions, so auroras and shooting stars may be theirs, eruptions
   and impacts mine. All seed- and shared-clock-driven, so every player sees the same event without extra sync;
   shader-driven where possible, reusing ambient particle types and LandmarkEffectSystem, with no per-event
   allocations.
   - **Volcanic eruptions**: VolcanoCone landmarks erupt now and then: plumes, ash fall, lava fountains and flows,
     maybe fresh lava channels (walkable lava, harmful later).
   - **Shooting stars**: brief streaks across the night sky.
   - **Meteors**: rare visible strikes leaving an impact crater landmark, perhaps with glowing or metallic debris
     (Metallic or Crystal styles). Impact sites pre-seeded and baked rather than terrain deformed live, unless that
     proves cheap.
   - **Auroras**: night-sky curtains, biased to cold, high biomes (Glacier, Tundra).

## Queue (from the supervisor and Zach, 2026-10-28)

Batches in this order; each batch gets one evaluation session, one sheet and one report, with a commit per item.

1. **Fix batch (done)**:
   - cave-mouth seams and debris (done: 22133cb; small flaps left at the slit tops of a few mouths);
   - bright cave roots;
   - glow flora: a gentle pulse per species (crystals 0), phase per plant, and the glow-list lights fading in and out
     with a small swell, with hysteresis at the edge of the nearest 16;
   - hot-rock (Ashlands, volcano slope) glow 30–40% lower;
   - water: rectangular holes in concave bays (sweep the shorelines) and a water-level step at bridges;
   - lit windows z-fighting;
   - doors and buildings scaled from each folk's height (door ≈ 1.3× standing height);
   - collision on fences, pens and palisades;
   - cave drips splash on rock and ring only on water.
2. **Flora at biome borders**: done as hard realm edges (85ed8ef).
3. **Realms (J-026, J-030), habitability (J-027), town count (J-032)**: done; realms on since e2267f6, 10 towns.
   Next realm work: the new sub-biomes in pairs (plan in REALMS-PLAN.md), after the items below.
4. **Caves round 2 (done)**: W-23 bigger caves; pools fit their floors and cave water is under rock; more jagged
   caves and much more rubble; faceted metallic; ribbed ice (shaped and shaded); per-cave variation; egg clutches with
   floor Nest anchors (first pass: paler, glossier, sizes in polish); the shared flame (FireSystem.AddFlame); mines
   (straight timbered drifts, wall torches, Work anchors) with a mining camp beside each (companion landmarks);
   MovingLights for carried lanterns. Cave styles per realm follow from biomes; realm-only styles come with the
   sub-biome pairs.
4b. **Seats (done)**: benches round town squares and before inns, logs and stumps round camp yards, fountain rims and
   hall steps, each with a Sit anchor (AnchorKind.Sit: hips on the seat, facing out, Capacity side by side, Size the
   spacing); open greens (a shade tree) with Green anchors, fenced gardens none; Bench no longer baked.
5. **Polish batch (done)**: cave boulders of the cave's own rock and scree; angular metallic caves; glass crystals;
   cave waterfalls; paler glossy eggs; matte camp mud; torch-lit mine chambers; grainy sand and dirt; leafy crowns;
   fire pits with Flame effects and palettes as town centrepieces (Hearth anchors); wild beehives (HiveTree props,
   Hive anchors, Anchor.OwnerLandmark); two moons (8-day phases, a small blue moon on its own path; Day on WorldState);
   glacier ice arches; the building pass (grounding and dirt bands, foundation skirts, matte stone, door porches);
   ring lanes round human villages (2a6cdd3).
5b. **Landmark RNG (next, after kind-possum's overnight benches)**: an independent random stream per landmark type
   in LandmarkBaking, landed alone in one commit. One final reshuffle: re-run every island check and publish the new
   places and bench coordinates to kind-possum and the animal session before pushing (it changes the world
   fingerprint). After it, new landmark types no longer move the rest of the island.
5c. **Towns grew over time (after 5b)**: houses on both sides of the ring and along the spokes inside it, with gardens
   and pens filling the interior; varied setback and spacing (clusters, gaps, a few turned slightly); the ring smoothed
   into an irregular loop following the terrain; a lane or two ending at a building instead of closing the loop.
6. **Small landmarks**: W-24 ruins and graveyards; hermit huts and camps; W-25 cold fog; W-26 salt-flat landmarks.
   Then the sub-biome pairs (Fungal + Ember first).
7. **Long term (when the queue runs dry, always batched)**:
   - Goal 1, enterable buildings: hinged doors with a Door anchor; outdoor clutter and seats with Sit anchors
     (benches, stools, logs, flat rocks); then hollow interiors.
   - Goal 2: one large city per folk: more building types, settlements on uneven ground (stepped pads, stilts, cliff
     edges), then the cities themselves (dwarf mountainside, elf canopy, human walled river city, goblin canyon, ogre
     crater).
   - A road query for travellers (next junction, Gate or landmark, signpost choices), when the animal session asks.
   - Fewer distinct prop species (perf (c)), only if towns are still slow, and with Zach's sheet.

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
- 2026-10-27: Phase 3a, **Anchors**. `Anchor`, `AnchorKind`, `AnchorSetBlob` and `Anchors.Near` / `Anchors.Pick`
  (Terrain/Anchors), baked last into `TerrainSource.Anchors` with a 32 m column grid (64 m since bfe933d). One field beyond the agreed
  contract: a `Biome` byte (the Tags byte can't hold both the style/folk and the biome). Cave anchors: a Roost on
  every chamber ceiling over 2.5 m up (capacity ≈ 3 per metre of chamber radius), a Nest on a wall ledge per chamber
  and one just inside each mouth, a Burrow at a wall's foot where the style's floor is soft (CaveStyle.Burrows: on for
  Limestone, Sandstone, Fungal, Rock). All sheltered. This seed: 104 anchors (39 Roost holding 583, 53 Nest, 12
  Burrow), all placed correctly (roosts against rock with air below, burrows on floors, nests in air within 0.6 m
  of a wall); Near matches a brute-force scan in 2,000 queries, Pick never returns a wrong anchor.
- 2026-10-27: **Glacier** biome (confirmed by the user: high, cold country). Appended last to the terrain's land
  biomes (World.unity), so earlier biomes keep their share of each region's roll. Climate: altitude 0.72–1,
  temperature 0–0.12, humidity 0.3–1; a broad rounded ice sheet (base 120 m, relief 90 m) of snow with GlacierIce on
  slopes and in patches, rock on cliffs; no plants; Tundra's fauna for now (the animal session may retune); caves of
  all three sizes at high density, all in the Ice style. Reshuffle on this seed: 24 of 196 regions changed biome — 16
  became Glacier (11 Tundra, 3 SparklingForest, 2 Mountains) and 8 flipped elsewhere as the draw shifted (7
  Mountains → Tundra, 1 SparklingForest → FoggyMarsh). Glacier now holds an ice CaveNetwork (418 m, 11 chambers),
  a cavern and four grottos.
- 2026-10-27: Cave look rework from Zach's reviews: geometry (033fc81: flat stepped floors, jagged ceilings, per-style
  shape), materials (bdb4071: floor materials, shared detail texture, sparse cracks, frosted blue ice, obsidian lava
  tubes; one shader and material for all styles), profiler rounds 2–3 (6670c12, aae8f42, bfe933d: FloorUnder thin
  floors and fewer calls, mouth shadows, 64 m anchor cells). CaveTools (7b29f7b): cave-only re-bake in play mode in
  34 ms, Visit presets, Check. Lava pools (575986b): basins in lava-tube chambers, drawn with the terrain water
  material's lava path, warm wall light from the nearest 16; lava itself now flows in warped swirls, mostly orange.
  Lava is walkable, cave and surface alike (Zach); TerrainSource.LavaAt for a future health system (e9df26b).
  Speleothems (c696ac1): tapered rods in the rock field, no meshes or draws of their own; 78 on this seed, +4% on
  CaveDistance. Water-surface detail and shorelines added as phase 3b; world events as a late phase 8.
- 2026-10-27: User roadmap merged (via supervisor): building types sized to inhabitants, roads and signs (moved up
  into phase 4), landscaping and water, leader and religious buildings; cave sizes, per-biome styles (glacier ice
  caves, metallic and crystalline caves in the mystical forest), stalactites, underground rivers, glowing flora; an
  imp warren; creature hook points for the animal session.
- 2026-10-27: Phase 3b water surfaces (76dcd74): fine ripples drifting with the flow, glitter, shallow caustics, a
  thin broken lapping shore line, lantern-lit cave water, deeper orange lava. Cave streams sunk into smooth-banked
  channels, darker, less mirror-like (882441e, 968f055). Wet, darker ground above the waterline (5e971a1; terrain
  shader and mesh builder lent by the supervisor; stored as a negative skirt drop, vertex layout unchanged).
- 2026-10-27: Phase 4a, **settlement layouts**. `SettlementLayout` on `LandmarkType.Settlement`; `SettlementBaking`
  lays streets (cut into ≤10 m stretches) and plots facing them into `SettlementSetBlob` with a 32 m grid, from
  integer-seeded randoms. Per culture: human villages a square with lanes (cobbles), dwarf holds a grid (flagstones),
  elf groves a ring street round a green (pale paths), goblin and ogre camps huts round a yard inside a palisade
  (radius kept for phase 5). Roles baked now for phase 5 to fill: a Seat on the square's best side, Shrines (square
  or edge), a water feature spot mid-square, Landscaping (green plots that keep their plants), houses; the layout's
  other roles take plots by distance from the middle. Plot sizes scale with `FolkScale` (stored on the settlement for
  doors and eaves later). Placed props (inn, smithy, pens) keep their spots as reserved. `TerrainSource` levels
  streets across and plots to pads, paints streets; `FloraPlacement` puts each plot's building in its cell facing
  its street and keeps plants off streets and built plots; the random ring houses are gone. Town radii raised
  (human 72, dwarf 65, elf 70). This seed: 7 settlements, 175 plots (human 22, dwarf 38, elf 24, goblin 25–31, ogre
  10), baked in 75 ms. Cost: world-wide SampleGround/SampleHeight within noise (−0.4% / +2.2%), +4.3% for samples
  inside towns.
- 2026-10-27/28: Zach's play-test feedback (FEEDBACK-2026-10-27.md). Bugs: cave mouths (one cut rule,
  CaveClearance), the skirt through an ice-cave ceiling, water edges at chunk borders, cave streams, landmark pools as
  discs, volcano crystal haze. Polish: lava, ice and lava cracks, crisper water and ripple-driven reflections, puddles
  fading in, more columns, the great tree, glow lighting on terrain/flora/grass/creatures (Glow.hlsl, SurfaceGlow),
  rock and cliff detail, ice-cave variation, roots and drips, SSAO, rocks tilting to slopes. Grass gusts and cheap
  density tricks (density stays 1x). Leaf cards and leaf fringe tried and dropped (J-021, J-022). Phase 5a-c: framed
  doors, real windows lit at night, the leader's hall, shrines, gardens, dressed stone, town anchors (Door, Seat,
  Altar, Bench, Well). v0.6.0 released with world-features at c4ad333.
