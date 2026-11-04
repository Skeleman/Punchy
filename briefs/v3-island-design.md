# Design: island v3, water first

For Zach and the world lane. Read-only design, 2026-11-03. Follows `rework-geography-collision.md` (option G2).
Issues: #83 (stepped pools), #88 (lake shelf fall), #87 (flat house plots).

## 0. Ground rules

- v3 sits behind the island flag like v1 and v2: `-island v3` or PUNCHY_ISLAND=v3. v2 stays the default until v3
  passes its audit and Zach says so. No v2 code path changes, so v2 hashes hold while v3 is built.
- v3 gets new hashes. Landmarks move once. Inside v3, prefer tools that can grow (new feature types, new scoring
  rules) over keeping v3's own hashes stable between batches. Log the hashes anyway, to spot surprises.
- `TerrainSource` stays the single source of truth. v3 only changes what its blobs hold and how `SampleGround` and
  `SampleSurface` read them.
- Each step reads only what the steps before it made. No step goes back and edits an earlier step's output.
  That is the main fix: today, later layers reshape ground under water that was already decided.
- Everything stays seed-derived, so host and client build the same island from the same inputs.

**Bake order:** ranges, then biome field, then water sources, then rivers, then the hydrology table with its
features, then carve, then surfaces and flora, then places, then the audit. New code lives in an `IslandV3` class
next to `IslandV2`, with one file per step (`V3Ranges`, `V3Hydro`, `V3Carve`, `V3Audit`).

## 1. Mountain ranges first

**What.** Ranges are terrain objects, not a biome painted on a border. Each range is a spine with tapering ends,
spurs that turn into foothills, and passes. They are drawn before any biome exists.

**How.**
- Keep v2's realm layout: 5 climate families, warped Voronoi sites, the cold/volcanic "never neighbours" retry.
- Trace each realm border as a polyline. A range takes only part of a border (say 40-85% of its length, seeded).
  The rest of the border is open country, where biomes blend widely.
- Height along the spine: crest height times a taper. The taper is 0 at each end and climbs to full over
  600-1200 m, so a range rises out of the plain and sinks back into it. The width tapers with it.
- Crest detail: reuse the W-08 ridged noise (peaks and saddles along the line), but along the spine's length,
  not in world space, so peaks sit on the crest.
- Cross-section: a profile by distance from the spine (sharp crest, steep upper flank, concave lower flank),
  not the v2 clamp on the Voronoi gap. That clamp gives flat-topped walls with parallel edges.
- Spurs: 2-6 short side spines per range, lower and shorter, branching at 30-70 degrees. Their tapered ends are
  the foothills. Add low rolling bumps within 2-4 half-widths of the spine.
- Passes: 1-3 per range, where the crest dips to a saddle 20-40% of its height. Recorded, so roads can route
  through them and rivers can cross through them.
- Volcano and mesa borders keep their own kinds (cone chain, stepped mesa band) with the same taper rule.

**Data.** `RangeSpine { float2[] Nodes; float[] Crest; float[] HalfWidth; float TaperIn, TaperOut; byte Kind;
int2 Realms }`, `RangePass { int Spine; float Along; float Depth; float Width }`, `RangeSpur` as a short spine with
a parent. Baked to a range height field (`HeightmapBlob`, 1024 a side, ~9 m cells) plus a `Cliffness` byte field
(steepness of the range shape itself, 0..1). The spines are kept in the blob too, for roads and scoring.

**Biomes follow the ranges, with bleed.**
- Realm weight at a point comes from the border distance, but the blend width varies: narrow (~60 m) under a
  full crest, wide (300-600 m) where no range stands, medium in passes and taper ends.
- Bleed: in open stretches, biome cells near the border may take the neighbour realm's biome, with a chance
  that falls with distance. So a forest tongue reaches into grassland through a pass.
- Data: the biome map keeps its id per pixel and gains a `BlendRadius` byte per pixel. `SampleMap` blurs by that
  radius (fixed kernel, variable stride, so the cost stays flat).
- The Mountains biome is no longer painted on crests. Height and slope decide the mountain look (step 6).

**Cliffs: rock by slope, not by biome.**
- After the biome surfaces, a global rock pass in `SampleSurface`: scree from about 28-38 degrees, bare rock above
  about 38-45 degrees, with edge noise. It wins over biome slope layers above its threshold.
- Rock colour comes from a small geology table (new asset): one rock and one scree surface per realm family
  (granite, basalt for volcanic, sandstone for arid, dark slate for wet, pale limestone for temperate). Bleed blends
  the geology too, so it never changes along a line.
- Use the larger of mesh slope and `Cliffness`, so far LODs (smoothed normals) keep their rock.
- Altitude bands on ranges: alpine grass, then scree, then snow, by height above the range's foot. This
  replaces `HighlandSurface` in v3.

**Reuse.** `IslandV2` realm code (`Shuffled`, `Forbidden`, `Warped`, `Nearest`, `Coast`), the ridged noise,
`HeightmapBlob`, biome layers. **Replace (v3 only).** The ridge `amt` clamp, crest painting, `HighlandSurface`.

**Risks.** Range ends inside the sea or a lake (clip by the coast and lake bowl). Taper too short reads as a wall again: check with shots from the ground, not only from above. Bleed can make tiny biome specks: give cells a
minimum size. A global rock pass changes every biome's cliffs; some (glacier, dunes) may want an opt-out flag.

## 2. Water sources

**What.** Every river starts at a listed source. No river comes from catchment alone.

- **Central lake.** Keep v2's bowl beside the spawn, but authored: a shoreline from noise, a fixed surface level,
  and one chosen outlet (the lowest rim point with a downhill route to the sea).
- **Tarns.** Small pools (20-60 m) in cirques on range flanks. Candidates at 55-85% of crest height, scored by
  flatness, a bowl shape on the range field, and the slope below (somewhere for the stream to go). 1-3 per range.
- **Springs.** At the foot of a cliff or the end of a spur, for realms with no tarns (so lowlands still get
  streams). A small seep pool.
- **Glacier snouts** in the cold realm (a tarn with a meltwater look).

**Data.** `WaterSource { byte Kind; float2 Centre; float Level; float Radius; float Outflow; int Body }`. Each
source becomes a body in the hydrology table (step 4).

**Reuse.** The v2 lake bowl and `LakeShiftM`. **Replace.** `RiverBaking`'s lake finding (filled hollows) and
`SourceHeight`/`SourceCatchment` springs.

**Risks.** Too few sources makes a dry island: set target counts per realm and log them. A tarn on a crest saddle
can spill both ways: pick one outlet only.

## 3. Rivers, source to sea

**How.**
- Route on a coarse grid. Reuse `PriorityFloodJob` and its heap on the range-plus-base ground (16 m is fine for
  routing). It gives every cell a downhill direction that always reaches the sea or the lake.
- Trace only from sources. Each trace follows the flood directions until it reaches the sea, the lake, or another
  river (a confluence). Width grows with the flow summed at confluences.
- Turn each trace into a vector path: smooth it (corner cutting), add meander that scales with width (the W-53
  idea), then resample every 4 m. Sample the real ground at each node.
- Per node: bed height, water level, half width, flow direction. Water level never rises downstream. It steps
  only at authored features (step 4); elsewhere the bed is cut to keep the slope under a limit.
- A river that meets a range crosses at a pass, or follows the foot of the range (the flood decides).

**Data.** `RiverNode { float2 Pos; float Bed; float Water; float HalfWidth }` in one array; `RiverPath { int First,
Count; int FromBody, ToBody; int JoinPath, JoinNode }`. This is the grand falls node format (x, level, z, half
width) made general. A 32 m grid indexes which segments touch each cell.

**Reuse.** `PriorityFloodJob`, the heap, the meander scaling, `RiverQuery` fields (so `SampleGround` callers barely
change), the grand falls staircase sampling. **Replace (v3 only).** Catchment rivers, the jittered and the drawn
river maps (one map now, used for both placing and drawing), `LowerBreachedLakes`, `ReshapeLoweredLakes`,
`FillRiverPits`, `CutDams`. Those repair passes exist because the ground came first.

**Risks.** Two paths running side by side a few metres apart (merge when closer than their widths). Long straight
runs across flat land (meander must see width and flatness). Bake time: 4 m nodes are cheap (a few hundred
thousand); keep them in a Burst job. Determinism: the trace order must be fixed (by source id), never by thread.

## 4. One hydrology table

**What.** One blob holds every water body. Each body has its own surface and knows its neighbours. `SampleGround`
asks the table which body holds a point, instead of taking the highest of seven sources.

**Data.**
- `HydroBody { byte Kind (Sea, Lake, Shelf, Tarn, Pool, StepPool, Plunge, River); float Level; int Shape;
  int Outlet; int Look }`. Lakes and pools have a shape (a shoreline polygon, or a centre plus radius plus noise);
  river bodies point at their node range and take the level from the nodes.
- `HydroFeature { byte Kind (Fall, StepChain, Shelf); int Upper, Lower; float3 Lip; float2 Out; float Drop;
  float Width }`. The lip is the boundary: above it the upper body owns the point, below it the lower one.
- A spatial grid (16 m) listing the bodies and features that touch each cell.
- Rule: at any point, at most one body owns the water. Where two bodies touch, the feature between them decides.

**Authored features, placed by scoring.** Each feature type is a scorer plus a builder, so new types plug in.
- **Falls.** Score each river stretch: drop of 6 m or more within 15 m of path (on the river's own profile), rock
  under the lip (`Cliffness` or rock slope), flow (width), view (a flat, reachable spot 60-150 m away with a clear
  line to the curtain, extra for a road or settlement in sight), and spacing from other falls. Take the best few
  per realm. The builder sets a rock lip, a plunge pool and the curtain effect.
- **Stepped pools (#83).** Score hillsides of 4-12 degrees with a small stream or spring, room for 4-12 pools, and
  a biome that suits a style: paddies near settlements, travertine terraces in arid and volcanic realms, mossy
  pools in wet ones. The builder sets each pool's level from the one above minus 0.4-1.5 m, with a low lip
  between, and joins the tail back to a river. The chain is one feature, so its pools never lose their banks.
- **Lake shelf (#88).** Score stretches of the lake rim where ground climbs 15-40 m within 100 m of the shore, over
  200-500 m of length, with an inflowing river and a good view from the shore or spawn. The builder adds a
  `Shelf` body: a higher sheet of water behind one long lip that spills as one continuous wide curtain into the
  main lake. The river that feeds the shelf is re-aimed to end in it.
- **Grand falls** becomes a fall feature with a high score bonus for height, found by survey on any seed, not
  fixed coordinates.

**Reuse.** The grand falls tier and pool model, `LandmarkWaterfall.shader` (the shelf needs a wide-curtain mode),
`FallWater`'s idea (upper water up to the lip), the water look blend. **Replace (v3 only).** The per-point
"highest wins" chain in `SampleGround`, `Terrace`, biome ponds and `PondMask` (swamp pools become `Pool` bodies),
landmark pools as a separate source (they become bodies, step 7).

**Risks.** This is the biggest change to the hot path: `SampleGround` runs per vertex. Keep the query to one grid
lookup and a few bodies. A wide shelf curtain is a new effect shape (cost and look). Scoring may find no site on
some seeds: every scorer logs its best score, and a feature with no site is skipped, not forced.

## 5. Carve the ground to fit the water

**What.** Water decides the ground. For each body, in a fixed order (sea, lake, shelf, rivers downstream first,
pools), cut a bed and shape banks so that:
- inside the body, ground is at least its depth under the surface;
- on a bank ring, ground is at least 0.3 m over the surface (raise a low levee where the natural ground is lower,
  so water can never spill sideways);
- outside the bank ring, ground blends back to the natural shape over a width set by the body's size.

Lips get a hard rock step. Plunge pools get a bowl. Rough banks (W-43c noise) are part of the carve, not a late
switch, so placement and drawing see the same banks.

**Data.** The final ground goes into one baked heightmap, 4 m cells over the island (about 2300 a side, ~11 MB
as ushort, shared by every world in the process as now). Fine detail noise stays analytic on top. Lips and falls
stay analytic too, because bilinear 4 m sampling would round off a sharp step.

**Reuse.** The `UsesHeightmap` path (a heightmap that already holds carved channels), `RiverSampling.Carve`'s bank
profile, the per-process bake sharing in `IslandV2System.Shared`. **Replace.** Carving at sample time.

**Risks.** Memory and bake time (5 million samples: Burst parallel, expect about a second). Levees on flat land
may look like dykes: keep them low and wide. Detail noise added after the bake can poke above a surface: fade it
to zero near water.

## 6. Biomes, surfaces, flora

- Biome weights come from the step 1 field. Surfaces as today (biome layers), then the rock pass, then water
  edges: river gravel, lake beach, wet bank, mossy lip, by distance to the nearest body (a new field in
  `GroundContext`).
- Flora and props as today, plus water dressing driven by the table: cattails on calm banks, driftwood on bars,
  mist and wet moss at falls, reeds at tarns, ice rims on cold pools. More small details per body type (the
  details tenet).
- **Reuse.** `BiomeSampling.EvaluateBiome`, flora placement, river dressing (W-65b). **Risks.** Little new; check
  that water-edge surfaces don't fight biome water-depth layers (pick one owner).

## 7. Landmarks, settlements, audit

- **Places on the final ground.** Landmarks, settlements, roads, caves, fishing villages, turtles and sea folk all
  bake against the finished v3 ground and water. No jittered map, no ponds-off world. `LandmarkBaking`'s evaluate
  job is reused as is. Roads may use the passes from step 1.
- **Landmark stamps and pools.** A landmark that brings a pool adds it as a body through one `AddBody` call that
  carves locally and checks its banks. Stamps may not change ground inside any body's bank ring.
- **Flat house plots (#87).** Each house plot gets a pad: a level platform the size of the footprint plus a margin,
  with a cut-and-fill skirt (steeper than the blend today, with a stone retaining edge on steep sides). Pads are
  carved before the audit, so the audit sees them. Plots on slopes over a limit move or drop, rather than make
  a tall skirt.
- **Audit gate.** `V3Audit` runs at the end of every v3 bake and logs one line `[hydro audit]`. It samples every
  body's shore at 2 m and the rest at 8 m, and counts:
  - water over ground: a surface higher than the ground next to it with no body there (spills, walls of water);
  - land between surfaces: a dry strip between two bodies whose levels are both above it;
  - dry pools: a pool body less than 0.2 m deep anywhere inside;
  - open river ends: a path whose last node is not on a body or the sea;
  - floating water: surface more than 1 m over the bed with no bed under it.
  Pass is zero in every count (a small tolerance only for floating water at curtains). A failed audit marks the
  bake; world cards are not done while it fails. The same probe runs on v2 for a baseline.

## 8. Multiplayer: island version from host to client

- Today the island is not synced, and the world fingerprint in `WorldSync` does not include the version, so a v2
  client could join a v3 host and see a different island.
- **Step A (small):** add the island version and the v3 hydro hash to the fingerprint. A mismatched client is
  refused with a clear message ("host runs island v3").
- **Step B:** the host tells the client its version when it connects. A client on another version rebakes that
  island before it spawns (the per-process bake cache is keyed by version), then sends its fingerprint.
- Determinism: v3 bakes must give the same result on every machine. Fixed job order, no shared random state,
  and check whether Burst float results can differ between CPUs (if so, compile bake jobs with deterministic
  float mode).

## Phased plan (one agent card each)

| # | Goal | Done when | Size |
|---|---|---|---|
| B0 | Water audit probe on v2 | `[hydro audit]` counts logged for v2; baseline in the brief | S |
| B1 | v3 flag and skeleton | `-island v3` bakes a copy of v2 through `IslandV3`; version in fingerprint (8A) | S |
| B2 | Range spines, taper, spurs, passes | range field baked; ground shots show no walls; spine and pass log | L |
| B3 | Biomes follow ranges, bleed | blend radius map; bleed through passes visible in a map export | M |
| B4 | Rock/scree by slope, geology, altitude bands | cliffs show rock in every realm, far LODs too | M |
| B5 | Hydro table, lake and tarns | `SampleGround` reads lake and tarn bodies from the table; audit clean on them | L |
| B6 | Rivers as 4 m vector paths | every river has a source and an end; confluences; open ends 0 | L |
| B7 | Carve pass and final heightmap | banks and levees; audit water-over-ground 0; bake time logged | M |
| B8 | Falls feature with scoring | falls only at scored sites; grand falls found by survey | M |
| B9 | Lake shelf (#88) | one long shelf curtain on the lake; shot from the shore | M |
| B10 | Stepped pool chains (#83) | 3 styles; no dry pools; shot of a chain | L |
| B11 | Water-edge surfaces, swamp pools as bodies, dressing | ponds and pond mask gone in v3; dressing reads the table | M |
| B12 | Landmarks, settlements, roads on v3; house pads (#87) | all place types bake; pads flat; audit still clean | L |
| B13 | Port caves, sea folk, fishing villages, turtles, archipelago, icebergs | all present on v3 with logs | M |
| B14 | Version sync host to client (8B) | a v2-launched client joins a v3 host and sees v3 | M |
| B15 | v2 vs v3 comparison | side-by-side shots and audit numbers for Zach | S |

B14 can run any time after B1, in the multiplayer lane. B2-B4 and B5-B7 are the two long chains; B8-B10 can run
in parallel once B7 lands.

## What to keep from v2

- Realm layout: 5 families, warped Voronoi, forbidden neighbours, the lake beside the spawn.
- All biome, surface, landmark and species assets (v3 changes how they are placed, not what they are).
- `PriorityFloodJob` and heap, meander scaling, the grand falls staircase model and curtain shader.
- `LandmarkBaking`, `SettlementBaking`, `RoadBaking`, `CaveBaking`, StructureKit, the ruin kit.
- Archipelago and icebergs (own seed streams, baked last).
- The per-process bake sharing and the `[bake] stages` and `[island]` log lines.

## Open questions for Zach

1. Spawn: keep it on the central lake shore in v3?
2. How wet: rough targets such as 6-10 rivers reaching the sea and 10-20 tarns? Should the arid realm get dry
   riverbeds (no water) instead of rivers?
3. Ranges: should some realm borders have no range at all (open blends), and should peaks stay at today's
   110-170 m or go taller?
4. Stepped pools: farmed paddies near villages, natural travertine terraces, or both?
5. Waterfalls: a few big, memorable falls, or many small ones along every mountain stream?
