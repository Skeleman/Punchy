# W-44 notes: house variety study (read-only), 2026-11-02

Read on world-features. No editor, no build, nothing changed in the game trees.

## 1. How houses are made now

- **Everything is procedural flora.** A house is a `FloraSpecies` asset whose `Form` picks a builder in
  `Flora/FloraGenerator.cs`: `Hut`, `StoneHouse`, `Tower` (elves), `Tent` (goblins, ogres), `Lodge`, `Inn`, `Hall`,
  `Smithy`, `Ruin`. No prefabs. Each builder is one function of boxes, tubes, beams and sheets, using shared helpers
  (`Door`/`Doorway`, `Window`, `RoofTube`, `Beam`, `DoorMat`, `GroundOcclusion`). `Grow(species, variant)` builds
  `Variants` (1 to 8, houses use 4) shapes per species, each at 3 LODs, from a seed of species seed + variant + name.
  Variants only differ in size and small random details: every StoneHouse is the same square block.
- **Shapes today.** StoneHouse: square block (walls 0.65 of height), quoins, plinth and string course, low pyramid roof
  with a rounded lip (#32), a 4-sided chimney, door at +Z, windows on three sides (lit with Glowing Accents). Inn and
  Hall: rectangular, gabled roof with a rounded ridge tube (Z-41 bevel), filled gable triangles; the Inn has a porch on
  posts, the Hall a plinth, crossed ridge beams and two cloth banners (second accent). Chimney bevels and smoke are
  W-43's, in flight.
- **Per-race style** is just which species each `SettlementLayout` lists and their colours: humans Hut (w 1) +
  StoneHouse (w 0.4) and a Hall; dwarves StoneHouse + StoneHall; elves ElfTower; goblins GoblinTent + MudHut; ogres
  OgreTent + OgreLodge. Folk size comes from the layout (`FolkScale` for plots, `BuildingScale` for the mesh).
- **Collision.** Not from TerrainSource. TerrainSource only levels each plot to its pad (`SettlementSampling.Height`,
  3 m blend) and keeps plants off it. The building itself collides through `FloraModel.Colliders`: upright cylinders
  (x, z, radius, top) per model, e.g. four for a StoneHouse so its corners stop you, a row along a Hall. Roofs are
  stood on through the support grid built from the visible mesh (`PropSupport`). The floor height is the plot's
  `FloraPlacement.FloorHeight` (average of five samples, raised to the door's ground).
- **Settlement layout** (`SettlementBaking`): one `Random` per settlement seeded from the landmark key. Streets, then
  plots of `PlotSize` (human 9.6 x 10.8 m, dwarf 10.8 x 10.8 at folk 0.85, elf 9.6 x 9.6, goblin 8.4 x 8.4, ogre
  8.4 x 9.6 at folk 1.7), `PlotGap` 2.5, setback 1. Then `AssignRoles` (shrines, other buildings, green plots), then
  `PickBuilding` per plot: one weighted roll per plot among that role's species.
- **Which variant stands on a plot** is decided later, in `FloraPlacement` (cell RNG: `FirstModel + NextInt(Variants)`).
  **Door anchors** are baked before that, from the species alone (`FloraGenerator.DoorFront`), so today every variant
  of a species must keep its door at the same front distance.
- **Draw cost**: `FloraSystem` issues one `RenderMeshIndirect` per model per LOD. More distinct models = more draws;
  more detail in one model = more vertices only.

Useful fact: every plot size is a multiple of 1.2 m (8, 9 and 7 modules).

## 2. Options compared

| | (a) kit + floorplan generator | (b) more hand variants of today's builders | (c) authored mesh kit (Blender/prefabs) | (d) kit pieces instanced at runtime |
|---|---|---|---|---|
| Variety | high: plans x styles combine | low-medium, each new look is new code | high | high |
| Style consistency | strong: one set of proportions, pitch, trim | drifts form by form | strong if one artist | strong |
| Interiors later | good: the plan is data (rooms, openings) | poor: each form is ad hoc | good | good |
| Collision | generated per rectangle, same cylinder lists | as now | needs new colliders | per piece, many |
| Draw cost | same as now (pieces merged into one model per variant) | same | same if merged | bad: hundreds of instances per town, shadow casters |
| Work size | medium (about 4 batches) | small per variant, grows forever | large, new pipeline, breaks the all-procedural look and FloraPart shading | large |
| Fit with codebase | reuses helpers, Grow/LOD/support grid unchanged | trivial | poor | poor |

**Recommendation: (a), with two changes to the supervisor's view.**
1. The kit is a set of builder functions inside the flora generator, merged at load into one mesh per variant per
   LOD, not separate meshes or prefabs. Draw count stays as it is.
2. "Each piece carries its own interior face so interiors come later for free" is not true. Walls are already closed
   boxes; what interiors really need is door openings that are cut, floors, inner walls, a way to enter (camera,
   culling, lighting) and every machine agreeing which plan stands on a plot. The thing to build now is the **floorplan
   as data**, chosen deterministically per plot, that an interior builder can read later. Also: no general roof
   derivation (straight skeleton); one roof per rectangle with fixed join rules is enough and keeps the style.

## 3. The recommended kit

**Grid.** Module 1.2 m at folk scale 1 (the plots already are). A bay is 2 modules (2.4 m): doors, windows and
annex joins sit on bays. Wall height and roof pitch are per style, not per piece. Scaled by the layout's
`BuildingScale` like today.

**Pieces** (builder functions, each with a LOD rule): wall bay (plain / window / door), corner (quoin or post), plinth
and string course runs, gable end (triangle infill), roof slab (gable side), hip end, lean-to slab (porch or annex),
ridge tube (the Z-41 bevel), eave lip (#32), porch posts + deck + step, chimney (W-43 bevels), banner pole (seats and
inns only), door mat.

**Floorplan generator** (seeded by the variant): a list of rectangles on the module grid.
- Main block: 2x2 bays (square, hip or pyramid roof, today's look), 3x2 or 3x2.5 (rectangle, gable along the long
  side). The front wall is always at the species' fixed `DoorFront` distance.
- Optional annex (L or T): on a side or the back, never the front; 1 to 2 bays wide, at least 1 module narrower than
  the main block's side it joins, joined on bay lines, not flush with a corner unless the plan is an L.
- Optional covered entry: 1 bay deep, 1 to 2 bays wide, only round the main door.
- Whole footprint plus eaves inside the plot's half extents minus 0.6 m (human: about 7.2 x 9.6 m incl. porch).

**Roof rules.** Same pitch for every rectangle of a house. Main block: hip if square, gable along its long axis
otherwise. Annex: gable at right angles to the main ridge; since it is narrower its ridge is lower and simply runs into
the main roof plane (valley by overlap, opaque mesh, no booleans). Annex wider than the main depth is not allowed (it
would need a crossing ridge). Porch: lean-to under the main eave at a shallower pitch. Chimney: on a gable end wall or
the back half of a ridge, never in a valley or over the porch.

**Doors and windows.** Main door centred on a front bay of the main block. Windows on bays that touch no corner and no
join. A second (back or annex) door only as a later option, with its own Door anchor.

**Style per race/biome = same pieces, different data.** New fields on the species (or a small style asset it points
to): wall kind (stone courses / timber frame / wattle and daub), roof kind (slate / thatch / shingle), pitch, wall
height ratio, trim on/off (quoins, string course, crossed ridge beams), plus today's colours and accents. Humans:
timber or rubble walls, thatch, 45 degrees. Dwarves: dressed stone, slate, 30 degrees, low walls. Elves keep towers;
goblins and ogres keep tents and lodges (the kit is for walled houses).

**Collision.** One cylinder row per rectangle (like the Hall), radius half its short side, top at the eaves (F-36: no
cylinder up into the roof). Roofs via the support grid as now. The plot clearance check in `FloraPlacement` already
uses the plot rectangle, so it needs no change.

**Interiors hook.** Keep `HousePlan` (rectangles, door and window bays, rooms = rectangles) as a pure function of
(species, plan index). An interior builder later emits floors, inner wall faces and cut openings from it.

## 4. Hash and placement risk

- **Landmark list hash, roads, roadside landmarks:** unaffected. Settlements bake after landmarks; nothing here touches
  landmark placement.
- **Settlement layout (streets, plots, pads, ground):** unaffected as long as `PlotSize`, `PlotGap`, `Setback` and
  `Landscaping` stay as they are. Fit houses into today's plots; never grow plots for them.
- **Which species lands on which plot:** adding a House entry to a layout changes the weighted roll's outcome but not
  the number of RNG draws (one per plot when the role has any species), so later plots and roles do not shift. The
  only shift risk: giving species to a role that had none (it starts drawing). Editing a layout asset re-bakes.
- **Door anchors:** move for plots whose species changes (`DoorFront` differs). Rule for the kit: the front wall is at
  the species' fixed `DoorFront` for every plan, so anchors stay right until phase H3.
- **Variant choice:** today it comes from the flora cell RNG. For interiors and side doors the plan must be known at
  anchor-bake time and on every machine, so H3 moves it to its own draw: a scrambled hash of the landmark key and the
  plot's index in its settlement (scramble first: `math.hash` of a small uint has no avalanche). That changes which
  model stands on existing plots (visual only, no layout or landmark hash change). Warn the supervisor before H3.

## 5. Phased plan (world batches)

| Batch | What | Visible | Calls |
|---|---|---|---|
| H1 | New `House` form (appended to the FloraForm enum, never inserted) with main block 2x2/3x2, hip or gable, optional covered entry; one human house species added to HumanVillage at low weight. Collision rows, support grid check on the roof. | rectangular houses and porches in human towns | 30-40 |
| H2 | Annexes (L and T), valley-by-overlap roofs, chimney placement rules, window bay rules. | L/T plans | 30 |
| H3 | Plan chosen per plot from its own hash; `DoorFront` and Door anchors per plan; optional back door. Supervisor heads-up first. | same houses, new spread | 20-25 |
| H4 | Style data (wall kind, roof kind, pitch, trim); dwarf stone/slate style replaces StoneHouse in holds; human timber/thatch. | race styles | 30 |
| H5 (later) | Interiors from the plan (floors, inner walls, cut doors, entering). Needs its own design note. | interiors | large |

Each batch: probe numbers first (landmark hash unchanged, plot count unchanged), then a few framed shots.
