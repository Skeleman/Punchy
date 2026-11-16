# Parts and units (W-175, #415)

Status: Zach OK'd the proposal (11-15). Row 2 shipped the format with the market stall and the smithy (section 6).
Units move over family by family; the old buildings can wait, but the end state is every built thing as a unit.

Aim (Zach 11-15, paraphrased): a placed unit such as a smith's tent or a vendor stall is made of standalone parts
(awning, table, anvil, crates, sign). Each part is defined once, units list their parts, and changing the anvil
changes every anvil. The same unit format is the blueprint the Model Lab saves (M-06 row 5).

Shares (rule 33): the W-166 material library (MaterialFamily, Shaders/Materials/*.hlsl), StructureKit (KitPart),
#399 / W-173 row 3 (every building from kit pieces), the anchor system (AnchorKind).

## 1. What builds props today

Five separate builders draw props. Each has its own primitives and its own way of naming a material:

| Builder | Module | Primitives | Material named by | Units built with it |
|---|---|---|---|---|
| Flora species | `Flora/FloraGenerator.*`, `FloraMeshBuilder` | Box, Tube, Blob, Sheet, Cloth | `FloraPart` + `b.Family` + `b.Wood` | Smithy, Stall, Inn, Hall, Tent, Pen, Stable, Well, Signpost, Woodpile, Fountain, Bench, Torch, FirePit, Hanging, MineFrame |
| Structure kit | `Terrain/Landmarks/StructureKit`, drawn by `FloraGenerator.Kit` | KitPart boxes (piece, material, centre, size, rotation, Solid) | `KitMaterial` -> `MaterialLibrary.FamilyOf` | houses, stilt houses, docks, wharf, shore and alpine clusters, reef, hazard houses, marsh huts, street lights |
| Dungeon props | `Fauna/Dungeon/DungeonProps` | Box, Drum, crystal in a fitted box | slot (Wood, Iron, Stone) on a Cave material copy | Crate, Barrel, Urn, Coffer, OreCart, Well, Altar, Lever, Sconce, MineLamp, Banner, SkullNiche |
| Colossus gear | `Fauna/StoneColossi.*`, `ColossusRigs`, `DunesColossus.MeshBuilder` | its own boxes and drums at giant scale | colour constants | the Ash Smith's anvil and trough, the Ice Mammoth's and Lantern Stag's loads |
| Held gear | `Fauna/CreatureRenderSystem.PoseGear` | drawn per creature per pose | colour constants | bucket, sack, basket, lantern, tongs |

The kit is the closest to the proposal: a part is a record (piece, material, place), not code. Its weak spot is
that a prop has no name of its own. The barrels by a kit house door are `KitPiece.Column` in `KitMaterial.Wood`
(StructureKit, the door-barrels block), so nothing downstream can tell them from a post.

## 2. The same part built in more than one place

Counted from the builders above, one entry per separate piece of geometry code (not per call).

| Part | Builds | Where |
|---|---|---|
| Lantern / lamp | 6 | kit Lamp/Flame parts (door lanterns, street lights, tunnel lanterns), Props Torch and Hanging, DungeonProps Sconce and MineLamp, colossus lanterns (ColossusRigs, LanternStag), PoseGear held lantern |
| Crate | 4 | DungeonProps Crate (planked, framed, braced), StructureKit wharf stack, StructureKit sunken goods, the crate carried in pier work (PoseGear) |
| Basket | 4 | StructureKit (shore, alpine, sea raft), BatFolkMeshes, PoseGear, Puffball colossus |
| Barrel | 3 | DungeonProps Barrel (staves, hoops), StructureKit door barrels (as Column), ColossusRigs |
| Bucket | 3 | Town Well, DungeonProps Well, PoseGear well keeper |
| Chimney | 3 | `KitPiece.Chimney` (StructureKit), `ChimneyTop` (Village; called by Smithy and the kit stone house), the hut chimney in FloraGenerator.Houses |
| Trough | 3 | Stable, Pen (Village), Ash Smith quench trough |
| Sign | 3 | Signpost species (Town), Inn hanging sign (Village), cave sockets / bat folk signs |
| Anvil | 2 | Smithy (wood block plus iron box), Ash Smith (house-sized) |
| Well | 2 | Town Well species, DungeonProps Well |
| Bench | 2 | Props Bench species, StructureKit door bench |
| Awning | 2 | Stall's striped cloth (Sheet + Cloth), SandHouse slatted shade porch (kit boxes) |
| Ladder | 1 | already one part: `KitPiece.Ladder`, drawn by FloraGenerator.Kit, read back by Climbing and cave mine ladders |

The ladder shows the pattern working: one definition, one look, and other systems (Climbing, the cave baker) read it.

Uses are coupled by hand. `AnchorBaking.AddWorkSpots` guesses where the smith stands from the Smithy's mean size
("within half a metre or so of each variant's own"). `WharfSpots` and `CreatureBrainSystem.Pier` keep their own
crate spots. If a part carried its use, the anchor would come from the actual part.

## 3. Proposal

### Part: one definition per thing

```
PartDef  id ("anvil")            stable string, used by units, the world file and the Model Lab
         Build(sink, seed, size) the geometry, drawn once, through a primitive sink (below)
         Family                  W-166 MaterialFamily per sub-piece (anvil: Wood block, Metal body)
         Bounds                  nominal size; units scale within a declared range
         Collider                None | Box (from bounds, default) | Boxes (listed)  - box collision, as the v3 rework
         Use (optional)          an AnchorKind plus local spot and facing: anvil -> Work, bench -> Seat,
                                 well -> Well, stall counter -> Stall, chimney top -> Perch, ladder -> climb
         MinScreen (optional)    screen-size cut for small parts (rule 20), see LOD
```

Parts stay C# builders because their shapes are procedural and seeded. A part registers once in a `PartLibrary`
(id -> PartDef). The anvil is written once and Smithy, the Ash Smith (at scale) and a dungeon forge all draw it.

**Primitive sink.** A part draws through a small interface (Box, Drum/Tube, Sheet, Blob, each taking a
MaterialFamily), not straight into one mesh type. Three adapters cover today's builders: FloraMeshBuilder (sets
`Family` and the bark mark), the dungeon prop part list (family -> Cave material slot), and the colossus
MeshBuilder (with scale). Because W-166 gives every family one include, a Flora-drawn anvil and a Cave-drawn
anvil get the same look. PoseGear stays separate for now: it re-poses every frame, so it would read the part's
mesh as a cached item mesh later rather than build inline.

### Unit: a list of parts (the blueprint)

```
UnitDef  id ("smithy")
         Parts[]   part id, offset, rotation, scale, family override (optional)
                   chance (0..1, seeded optional part)  group (pick one of a variant group)
         Builders[] optional coded pieces that are not parts yet (the Smithy's walls and roof), so a unit moves over
                   piece by piece
         Seed salt so each part draws from its own stream: adding an optional part never shifts another's draws
                   (the same rule as the kit's door barrels)
```

Units are data: a serialisable struct, saved as a JSON TextAsset. A coded unit and a Model Lab blueprint are
the same thing, so the Lab never needs its own format (M-06 row 5). Nested units are allowed one level deep
(a market is units of stalls) so blueprints stay readable.

### How other systems read them

- **Baking.** A unit variant (id + seed) bakes into one mesh per family set, as the species bake does now, so
  draw calls don't grow. A part's mesh is cached by (part id, seed, size bucket) and reused across units.
- **World file (rule 22).** A placement stores the unit id, its seed and a list of per-part overrides
  (removed, moved). It does not store the mesh. A map edit can then swap a stall's awning or delete a crate, and
  every machine rebuilds the same unit. Units keep the current placement keys, so Landmarks_V3 is unchanged
  until a row changes what gets placed.
- **LOD (rule 20).** Each part has a `MinScreen` in `DetailPolicy.ScreenSize` terms. Goods on a counter and
  hanging tools drop first, the awning and walls last. The far impostor or low tier bakes only parts above its
  cut. No fixed metres.
- **Anchors and use.** `AnchorBaking` emits a part's Use as an anchor at the part's real spot in the unit. That
  replaces the mean-size guesses (AddWorkSpots) and the hand-kept crate spots.
- **Model Lab (M-06 row 5).** The Lab lists `PartLibrary` as the palette, places parts against the person
  for scale, and saves a UnitDef. Kit gaps it finds (missing parts, sizes that won't snap) feed back as new
  PartDefs.
- **Ownership (later, NPC ownership card).** A unit's Container parts (crate, barrel, coffer) are where a
  household's items live, which gives theft a place to happen.

## 4. Order for row 2 onward (after Zach's OK)

1. PartLibrary, the sink and the FloraMeshBuilder adapter, plus anvil, crate, barrel and bucket parts. Smithy
   and Stall become units. Pixel-diff shows no look change except the shared crate and barrel.
2. Dungeon adapter: DungeonProps Crate, Barrel and Well draw the same parts. Kit door barrels stop being Column.
3. Camps and docks (Tent, FirePit, wharf crates, shore baskets). Lanterns become one part across kit, props and
   dungeons.
4. Anchors from part uses (retires AddWorkSpots' size guess). Colossus gear on the scaled adapter.
5. Model Lab reads PartLibrary and saves UnitDefs (M-06 row 5).

Each row keeps Landmarks_V3 or re-pins it with the reason.

## 5. Risks and choices for Zach

- **Code parts, data units.** Parts as data would let the Lab author new shapes, but then procedural detail
  (staves, braces, seeded wear) would need a mesh format. Cheaper: parts stay code, units become data. The Lab
  composes but doesn't sculpt.
- **Small look changes are intended.** Today's three crates differ, and unifying them changes how two of them
  look. Each row's pixel-diff names these.
- **Bake cost.** Bake cost should be flat or lower, since part meshes are shared. A BENCH job checks the bake
  time after row 1 of the move, not this row.

## 6. What row 2 built

- `Flora/UnitDef.cs`: `UnitDef` (id, depth as a share of width, parts in draw order) and `UnitPart` (part id, and
  `At`, `To`, `Span` as shares of the unit's frame plus metres). Plain `[Serializable]` data, so JsonUtility
  round-trips it (the Model Lab's save format). `UnitFrame` turns shares into metres for one grown variant.
- `FloraGenerator.Parts.cs`: the `PartLibrary` (id to builder) and `BuildUnit`. Each part says what its points mean
  and names its own `MaterialFamily`. Parts so far: pole, post, counter, wares, awning, pyramid roof, forge hearth,
  chimney stack, anvil.
- `FloraGenerator.Units.cs`: `StallUnit` and `SmithyUnit`, which replaced the coded `Stall` and `Smithy` with
  identical meshes (every variant's three LODs, colliders and perches hash the same before and after).
- `UnitLibraryTests`: every part a unit names is in the library, unit ids are unique, units survive JSON.
- To move a family over: write its pieces as parts (reuse a part when one exists; any coded piece can be a part,
  which is how a unit moves over piece by piece), list them in a `UnitDef`, point the form's case at
  `BuildUnit`, and prove the meshes hash the same.
- **Not yet:** parts still share the variant's random stream, so the part order is part of the look (per-part seed
  salts come with a re-pin). The primitive sink and its dungeon and colossus adapters, the shared crate, barrel and
  bucket, optional parts (chance, group), per-part `MinScreen` and `Use` anchors are later rows (section 4).
