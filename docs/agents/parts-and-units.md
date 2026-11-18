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

## 7. What row 3 built (camps)

- Tent and fire pit are units (`TentUnit`, `FirePitUnit`) with identical meshes. New parts: hide tent (poles and
  panels share their feet, so one part, and it sets the unit's bounds), door mat (wraps the `DoorMat` the houses
  and kit doors already call, so all three draw one mat), fire ring (ash bed, stones, collider; sets the bounds),
  log tepee, embers (dropped at the far level of detail).
- A part may set `g.Width`, `g.Height` and `g.TrunkRadius` after `BuildUnit`'s defaults: that is how a shell part
  keeps the old bounds, so placement and Landmarks_V3 don't move.
- **Docks and lanterns wait for row 3b.** Wharf crates, shore baskets and the six lanterns are `StructureKit`
  boxes, not flora builders, so they need a kit adapter (a new partial beside `StructureKit`, which is over
  the 2500-line limit). Unifying them changes how most of them look, so 3b is a look-change row with a pixel-diff.

## 8. What row 3b built (kit crates, baskets, lanterns)

- `StructureKit.Parts.cs` (a partial; `StructureKit` became `partial`) holds the kit's small parts, each defined
  once: `Crate` (body and lid band, the band turning with the crate), `Basket` (net body and wooden rim) and
  `LanternHead` (cap at 45 degrees, glass, drip tray; `Lamp` or `Flame` glass, a scale and a roll).
- Sites moved: wharf crate stacks, sunken trade-goods crates, sea raft baskets, and all six hung lanterns
  (wharf house door, shore post, fixture lantern, pier lamp, cave wall lantern, inn door). Each site keeps its own
  hanger or arm, glass centre and seeded draws (same calls in the same order), so only the parts themselves change.
- Intended look changes: every hung lantern now has a cap and a drip tray (before only the fixture lantern had a
  cap and only the wall lantern both); inn and pier glass grew from 0.22 x 0.30 to the shared 0.24 x 0.32; the wall
  lantern is the same head at 0.7 scale (glass 0.17 x 0.22, was 0.15 x 0.24); wharf lid bands now turn with their
  crate (they stayed square to the dock before); sunken crates and raft baskets gained the band and rim.
- Not moved: the alpine sled's lashed bundle (a bundle, not a basket), sconces, braziers, candelabra and beacons
  (other lights, not hung lanterns). Shore baskets did not exist as a site: the crab pot is its own cage.

## 9. What row 4 built (the part format)

- **One size rule.** `Span` is a part's full size in every axis; a round part's diameter is `Span.x`. Pole, post,
  pyramid roof, hide tent, fire ring, log tepee and embers used to read a half size; coded units doubled those
  values (exact in floats), so their meshes hash the same.
- **Turn and mirror.** `UnitPart.Yaw` (degrees about up) and `Mirror` (in x, applied before the turn) act about the
  part's `At`. `BuildUnit` moves the part's vertices, normals, colliders, boxes, perches, chimneys and windows, and
  flips the winding when mirrored so faces still point out. All-zero offsets skip the move, so coded units keep
  every bit.
- **Centred parts honour At.** Hide tent, fire ring, log tepee and embers draw about the unit's middle; their `At`
  is now an offset (it was ignored).
- **Bounds are opt-in.** A part sets `g.Width` and `g.TrunkRadius` only when `UnitPart.Bounds` is set (the coded
  tent and fire pit set it). A part placed in a blueprint no longer overwrites the unit's frame.
- **Metre frames.** `UnitDef.Host` names the species a blueprint grows on, `Size` gives a fixed frame in metres (zero
  grows from the host as before), and `WidthOfHeight` sets width as a share of height (zero keeps the host's
  spread).
- **A stream per part.** With `UnitDef.PartStreams`, each part draws from its own random stream (unit seed, part id,
  `UnitPart.Seed`), so adding or reordering parts never changes another part's look. Coded units leave it off until a
  re-pin.
- **Tidy JSON.** `UnitDef.ToJson()` writes floats in their shortest round-trip form (0.7, not 0.699999988); the
  Model Lab saves and copies with it.
- `UnitLibraryTests` cover tidy JSON, yaw, mirror winding and part streams.

## 10. What row 5 built (the building parts)

- `FloraGenerator.Parts.Build.cs` adds eleven parts: `wall`, `floor`, `door`, `window`, `gabled roof`, `beam`, `stair`,
  `rail`, `sign`, `lantern`, `crate` (list in `FloraGenerator.BuildPartIds`). Each lays out structure-kit pieces
  (`KitPart`) in the unit frame and draws them through the kit's `DrawKitPart`, so the host species' `KitCulture` sets
  trim colours and wall/roof looks and every piece takes its W-166 family from `MaterialLibrary.FamilyOf`.
- Lines (`wall`, `beam`, `rail`) run from At to To; the rest stand on or centre on At and read their size from Span,
  with a default in metres for any axis left at zero. Doors, windows and signs face +z; turn them with Yaw.
- Collision follows the kit (W-67): every piece that isn't decor is a box, and so is any piece marked solid (a crate's
  body, a door step). Lamps and glass stay walk-through, so a lantern has no box. `KitDecor` is the one rule for
  both kit buildings and these parts.
- `stair`, `lantern` and `crate` call the kit's own builders (`StructureKit.Stair`, `LanternHead`, `Crate`): one
  definition each (rule 33).

## 11. What row 6 built (nesting, snapping, JSON units in the world)

- **Nested units:** a part with id `unit` and `UnitPart.Unit` set draws that whole unit at `At`, turned and mirrored
  with the part. Its frame is the part's Span if it has one, otherwise the unit's own Size, otherwise the parent's
  frame. A nested unit always draws from part streams salted by its id and the part's Seed. Nesting stops at depth 4,
  so a unit that nests itself still grows. `DrawParts` uses salt 0 at the top level, so coded units hash the same.
- **`UnitLibrary`** finds a unit by id: the coded units, any JSON unit a species loaded, and (in the editor only) every
  blueprint in `Assets/Game/Flora/Units`. The Model Lab's Save calls `Reload` so nested copies pick up the edit.
- **JSON units at runtime:** `FloraSpecies.UnitJson` (a TextAsset) is read on the main thread in OnEnable/OnValidate
  into `JsonUnit`, which `GrowParts` uses when `Unit` is empty. Wherever that species is placed, it grows the
  blueprint. Settlements place them as of section 12.
- **Snapping:** `UnitDef.Snap(grid, join)` moves a metre-only point onto an earlier part's point when it is within
  `join` of it, then rounds metres to `grid`. The Lab's "Snap joins" button uses 5 cm / 12 cm. The Lab shows a
  "Unit id" field when a part is `unit`.

## 12. Units in settlements (W-175 world part)

- **A Lab blueprint in a town:** make a species for it (copy a building species such as `Stall`, give it its own
  `Seed`), set `UnitJson` to the saved JSON in `Flora/Units` and leave `Unit` empty. Then list that species where it
  should stand: a landmark prop (fixed `Count`, `Ring`, `Angle`, `FaceCentre`, `Clearance`) or a settlement layout
  building (`Role`, `Weight`). Placement and drawing come from the species as for any other; its own `Solid` decides collision.
- **First one:** `Cookhouse` (cookhouse.json) is a HumanVillage prop at ring 0.6, angle 70, between the inn and the
  smithy, facing the square.
- **Guard:** WorldTests `Blueprints_PlacedInSettlements` (hook `BlueprintPlacements`) fails when a species' JSON reads
  no parts, a filled `Unit` hides it, no landmark lists it, or it stands in none of the landmarks that list it.
- **Still to do:** converting the older code-built buildings to units (#415).

## Castle kit (W-182, #254)

- **Parts:** `curtain wall` (At to To, Span y walkway height, z thickness), `castle tower` (square, Span x side, y
  height), `gatehouse` (arch, Span = width, height, depth) and `portcullis` (iron, static) in
  FloraGenerator.Parts.Castle.cs. Stone goes through DrawBuildParts, so the host's culture sets the look.
- **Wear:** `UnitPart.Wear` ages any castle part: moss from 0.3, missing merlons, breaches, broken tops and rubble
  toward 1. The 12 `castle *` blueprints are the four units at 0, 0.45 (mossy) and 0.9 (ruined). Lab only so far.
- **Gate kit:** `castle gate` (arch, two flanking towers, portcullis down) and `castle gate raised` (portcullis up, teeth
  in the arch crown) are one unit in two static poses; syncing the portcullis would be a network change. A nested unit
  ages to at least its part's Wear, and `FloraSpecies.NestedJson` lists the units a species' JSON nests so builds have
  them. `castle ruin gate` (the `CastleGateRuins` landmark) nests the raised gate at Wear 0.8.
