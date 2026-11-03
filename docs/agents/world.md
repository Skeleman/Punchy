# World: terrain, biomes, landmarks, caves

Read before changing terrain, biomes, rivers, landmarks, settlements, roads or caves.

- **`TerrainSource` is the single source of truth** for the ground: height, biome weights, rivers, lakes, water
  and surface material at any point (`SampleGround`, `SampleHeight`, `SampleSurface`, `SampleNormal`). Terrain
  meshes, player movement, plants, animals, weather and particles all sample it, so they always agree. Don't
  compute heights any other way.
- It is **baked** from `TerrainAuthoring` in the `Assets/Game/Scenes/World.unity` subscene. Editing a biome or
  surface asset re-bakes the terrain, rivers included, which takes a while and can reshuffle the island.
- **Biome placement:** regions are warped Voronoi cells. Each cell reads a climate from smooth fields
  (`BiomeSampling.Climate`: altitude, temperature falling with altitude, humidity, and volcanism around a few
  hotspots in high country) and picks a biome weighted by `Frequency` × how well the climate fits the biome's
  Climate ranges, so arid biomes cluster, wet ones cluster, and each volcano sits inside ashlands. The region at
  the origin is always the first biome. Not every seed gets every biome; to force a layout, export the biome map
  (TerrainAuthoring inspector), paint it, and set Biome Source to Map.
- **Data-driven assets, not code, define content.** Biomes (`Assets/Game/Biomes`) list their surfaces, terrain shape,
  water look, weather odds, ambience, flora, fauna and ambient particles. Most new content is a new asset or a
  biome list entry. To thin an ambient particle type everywhere, set its `DensityScale` instead of editing biome
  densities (a biome edit re-bakes the terrain); `GroundTint` takes each particle's colour from the ground under
  it, and `Volume` (Mist) makes big soft cloud-like patches (dune dust).
- **Plants and animals are procedural, not models.** `FloraGenerator` grows every plant species in a few seeded
  variants at three LODs; creatures are assemblies of `LimbType` parts drawn as instanced tapered capsules. There
  are no imported meshes for either.
- **Landmarks** (`LandmarkType` assets in `Assets/Game/Landmarks`: oases, geysers, waterfalls, dens, rock
  formations…) are small places that combine a terrain stamp (dome, mesa, cliff, levelling, bumps), pools with
  their own water look, a ground surface, plants and rocks, effects (plumes, waterfall curtains) and resident
  animals. Biomes list them with a density per km²; `TerrainAuthoring.PlacedLandmarks` takes hand-placed ones.
  `LandmarkBaking` places them after rivers (well inside one biome, dry, not too uneven; every listed type at least
  once if anywhere suits it), and `TerrainSource` applies them, so everything that samples the ground sees them.
  This stamp-plus-contents model is meant to grow into hand-made content (settlements, roads, caves).
- **Caves** break the "one ground height" rule in a controlled way. A landmark type with `Cave` set gets a
  passage (`CaveBaking`: a chain of nodes from its cliff face, winding in and down to a chamber). Cave air is a
  signed distance (`CaveSampling`); rock is below the surface and outside every cave. `TerrainSource` answers
  `CaveDistance`, `IsCaveHole` (surface cut away at an entrance), `IsRock` and `FloorUnder(float3)` (the cave
  floor under a point, or the surface). Anything that can go underground must use `FloorUnder` with a 3D
  position instead of `SampleHeight` (player movement, the player's body in `CreatureAnimator`, the camera).
  `CaveDistance` has an exact sign but its size can overstate the distance to rock 2-9x (squashed floors,
  ledges, wall noise); carving and placement hash on it, so it never changes. For push-out, sphere casts or step
  sizes use `CaveDistanceBound` (`CaveSampling.LowerBound`): a lower bound (W-33 probe: 5 of ~10k points over,
  by at most 5 cm, all at stepped wall blocks), about 5x the cost. Retune `SlopeLimit` if `WallNoise` changes.
  At entrances the terrain stores each vertex's distance to cave air (Morph.w) and its shader cuts the surface
  just inside the cave's edge, in every pass (near LODs only); `CaveSystem` builds the cave walls with surface
  nets in background jobs, sets `CaveSystem.Underground` (weather stops) and lights a lantern on the player.
  Rubble and plants are placed per cave in `Parallel.For` and joined in cave order, so the output is the same as
  a serial pass; keep any new per-cave pass that way (no shared RNG or lists across caves). `[bake] caves ms`
  logs each step. `-whitepools` draws every cave water pool as white pool water (`CaveSystem.WhitePoolWater`,
  #35); murk above about 0.3 makes the pool a hard-edged opaque slab that reads as snow. White pools set `_SoftRim`:
  the rim fades, the middle darkens and stays see-through (body alpha x0.55), and the glow is a band inside the rim.
- **Bat folk (#35, W-18):** `BatFolkBaking` runs after CaveBaking, reads the caves only and writes its own blob
  (`TerrainSource.BatFolk`), so landmark and cave hashes never see it. A cave qualifies with a chamber >= 9 m
  at least 3 nodes in and a water pool (the chamber's own, else the nearest in the cave); up to 1 in 6 (min 1) is
  picked by a scrambled mouth-cell hash on its own seed stream. Its `WhitePools` (per cave pool) draws those
  pools white; `-whitepools` is now a debug override. Logs `[batfolk] caves N, picked K` (v1 player: from the blob).
  W-19: `Props` holds 3-6 hut pods (hung from the ceiling, anchor + stalk) and 4-8 racks/baskets per pick, from a
  per-site RNG (scrambled stream ^ key), kept clear of pools and the path segments through the chamber node;
  `BatFolkMeshes` builds them in the cave vertex layout, CaveSystem draws them with tinted cave-material copies.
  W-20: each hut's stalk is sized so the pod bottom hangs 3-6 m above the floor (in lantern reach; a low ceiling
  gives a short stalk and >= 2.6 m headroom), the stalk is checked for air along its length, and the log ends with
  `hut bottoms a/b/c m` per pick.
- **Cave previews in the editor:** a camera moved into a cave alone shows nothing (chunks stream, `Underground` and
  the lantern follow the player over a few frames). In play mode call `CaveTools.PreviewAt(pos, target, whitePools)`,
  wait a few seconds, then `CaveTools.PreviewShot(path)`.
- **Plants, animals and particles are not baked:** editing their assets in Play mode applies live within about a
  second (the systems hash their config and rebuild).
- **Ponds are pruned after baking:** `BuildPlaces` labels wet cells on a 16 m grid and bakes `PondMask`, which
  zeroes every pond under `PondMinCells` (3) cells. Landmarks are placed before ponds, so this never moves them;
  a new pond rule goes through the mask, not more erosion or threshold tuning.
  With a biome map (island v2) the core test uses the map's sharp weights: the blurred shape weights never pass
  0.4 in its small swamp patches, which left v2 with no ponds at all.
  Ponds above sea level are stepped pools (W-24/W-25, `TerrainSource.Terrace`): irregular ~15 m paddy cells, each
  level at its centre's `BaseHeight`, with a bank 0.3 m above the higher neighbour. Contour bands (W-24) were ~1-6 m
  wide and read as mud lines; cells keep the paddies wide on any slope. The 16 m pond
  grid labels on the pre-terrace dig (`PondDig`), or the banks split every pond and the mask drops them.
- Roads (W-16): the A* routes run in parallel on a managed copy of the cost grid with a precomputed wander array; Emit and bridge sampling run in parallel and are added serially in pair order, so output is identical. The `[bake] stages` line splits settlements into towns/roads/roadside/blob. Settlements went from about 970 ms to about 290 ms (editor bench, hash unchanged).

## Island version and waterfalls (W-22)

- v2 is the default island: no flag runs v2 (landmarks 125 / AFA9CC63, cave hash D531293D622E785A since the W-21
  biome-blend fix). `-island v1` or PUNCHY_ISLAND=v1 runs v1 (101 / E128CB1D). The island isn't synced over the
  network: host and client each read their own launch, so start both the same way.
- BiomeData.SampleMapBlurred sums every biome id's weight and keeps the top 4 minus the 5th. Keeping only the first 4
  ids it met made weights jump where a fifth biome came in, which raised vertical walls and pits.
- v2 map Mountains pixels cover only ridge crests, but the ridge height and blurred shape lift the ground well past
  them, so lowland surfaces climbed half of each flank. TerrainSource.HighlandSurface hands the surface to the
  mountain biome where the lift passes 40-52 m (map mode only; v1 untouched). Changing it moves landmarks.
- Above a fall's lip the lower pool's wide channel reaches back under the upper river (river cells are 16 m, channels
  up to ~50 m), so the lowest-carve rule picked the lower water and land showed between the surfaces.
  RiverQuery.FallWater keeps the upper surface up to the lip.
- A lake that LowerBreachedLakes drops leaves cells shaped for the old surface. RiverBaking.ReshapeLoweredLakes
  (W-35b) reruns the bed shaping on the cells whose base level dropped, so their water never stands above the ground
  or rises along the flow. It changes river and cave hashes on both islands, but not landmarks.
- Surface falls run their length in half the effect's lifetime (LandmarkWaterfall.shader); cave falls keep 1.62.
  LandmarkEffectSystem.FallSounds plays WaterfallRush loops at the 4 nearest falls, logged as `[falls] sound sources`.
- Icebergs (`Terrain/Icebergs`) are scenery from their own stream (seed ^ 0x1CEB3A65): they never touch landmarks or the cave hash. The beach strip on land has the "Ocean" biome as its dominant biome, so a sea-touches-biome test must cross it (IcebergBaking treats it as neutral shore for 320 m). Berg collision rides on `TerrainSource.TowerWall`/`Deck`.
- Grand falls (W-29, `Rivers/GrandFalls.cs`, v2 only): an overlay baked after every place, so hashes hold. Its water is a staircase along a polyline (a fall = two nodes 1.5-3 m apart), so the surface is continuous; pools apply only past their foot so they never cut the lip. The site is fixed coordinates for seed 8121: a new seed needs a new survey.
- Archipelago (W-30, `Island/Archipelago.cs`, v2 only): an overlay like the grand falls, baked after places, so landmarks, caves and settlements never see it. It only raises sea floor (applied first in SampleHeight/SampleGround) and turns its islets' surface to Dunes. The site comes from a ray survey of the dune coast, so it follows the seed. `-archipelago off|on` swaps it in a running shot list. The per-shot draw-call/tris recorders are not valid in a non-development player: read UnityStats in editor play mode instead.
- W-28b: bergs vary by a seeded shape class (IcebergData.Shape, Scale, Tilt, peaks, notch, ledge, overhang) drawn and stood on from the same IcebergShape functions; their colour comes from a runtime palette strip indexed by mesh u. TerrainSource.Deck also returns a salt tower step floor, because the player only uses FloorUnder in a cave.
- River pits (W-36, `RiverBaking.FillRiverPits`): a river bed below the bed it drains into backs its water up to that dam while its carve digs the banks down to the low bed, so the water stands above the banks (open ends). The pass raises such beds under their water, downstream first; lake and sea backwater are left alone. River `Gradient` (rapids foam) is the smaller of the bed and water drop, and lake-outlet foam scales with how far the surface really ramps, or flat backed-up water shows a white strip.
- `DunesColossus.MeshBuilder` (also used by `Mantrap`) takes `Tri(a, c, b)`: Ellipsoid/Dome/Limb were written with inward winding, so the corners are swapped in `Tri` (W-48). Unity front faces are clockwise seen from outside; check a new shape with cross(b-a, c-a) pointing out.

## Ruins (W-56, issue #24)

- Ruins are built from the ruin kit (`Landmarks/RuinKit.cs`): set pieces (entrance, chamber, sanctum, hall, stair,
  corner, dead end) described as data in their own frame (a along the entry, b to the left, y up from the entry floor):
  rooms (UV box, floor, ceiling, slope, kind 0 closed / 1 open to the sky), solid boxes, sand drifts, wall torches and
  exit sockets. A seeded builder (`RuinKit.Temple(seed)`) joins pieces socket to socket, tries side branches only
  where they clear every room, and the result is baked into the landmark blob (`RuinRooms`, `RuinBoxes`, ...), so host
  and client build the same ruin from the seed (`RuinKit.Fingerprint` checks it).
- The kit is meant to grow into cities, fishing villages and new races' settlements: keep pieces as data (a new biome
  or culture adds a piece factory and a plan, not new collision or drawing code). `SunkenTempleShape` answers floor,
  air, wall and cut from the blob only, and `SunkenTempleSystem` draws the same data, so collision matches the art.
- Open rooms cut the ground surface like a cave mouth (`TerrainSource.TempleCut`, `IsCaveHole`); closed rooms are air
  under the ground (`TempleAir`, `IsRock`). The swell over the temple is part of the heightmap, so the ground hash
  changes near temples only.
- Gotcha: rooms stack in plan (the lower corridors and the second stair run under the pillared hall), so anything
  that asks "which room is this point in" must pick by height too (highest floor within step reach of the feet),
  never the first room whose UV box contains it. W-56's walk probe did that and read a false 7.5 m sink; it also
  walked straight lines between door centres through a pillar row, which looked like a collision block.

## StructureKit (W-58, W-58b, issue #24)

`Terrain/Landmarks/StructureKit.cs` builds whole buildings out of boxes, from a seed and a culture trim, so new
building kinds are code plus a species asset, not hand-made meshes. Everything is in building space: +Z is the
front (the door side), y = 0 is the ground, metres.

- **Parts.** `House`, `Gate`, `Platform`, `Stair`, `Balcony` and `Tower` append `KitPart`s (`Piece`, `Material`,
  `Centre`, `Size`, `Rotation`, `Solid`) to a list. `KitPiece` says what a box is (Wall, Floor, Roof, Stair, Column,
  Arch, Platform, Balcony, Rail, Window, Door, Trim, Rubble, Moss, Beam, Chimney); `KitMaterial` picks its colour
  from the trim (Wall, Trim, Roof, Wood, Opening, Moss, Stone). Walls are `StructureKit.Thick` (0.4 m) thick; only
  ground-storey wall bays are `Solid`.
- **Culture trims.** `StructureKit.For(FolkCulture)` returns a `CultureTrim`: colours, roof shape (`KitRoof` Gable,
  Tiered, Flat, Steep), storey height, bay width, pitch, and the details: quoins, crenels, shutters, and the chances
  of a tower, balcony and outside stair. Dwarf: flat roof, crenels. Elf: tiered roof, 3.4 m storeys, 2.2 m bays,
  balcony 0.7, stair 0.3. Goblin, Ogre, SnowFolk: steep roof, shutters. Human (the default): gable roof, quoins,
  shutters. A new people is one more case in `For`.
- **House.** `House(seed, trim, width, depth, storeys, decay)` lays bays round four walls (door bay in the middle of
  the front, windows by chance, more at the front), string courses, quoins, the roof, then a balcony and an outside
  stair by chance. `decay > 0` makes a ruin: walls broken down to a slope, rubble, moss caps, ivy strands, empty
  window holes, a few fallen rafters, no roof or door. `Fingerprint(parts)` hashes a part list for regressions
  (seed 42, Human, 8 x 7 m, 2 storeys: 203 parts).
- **Stair and Platform.** `Stair(parts, at, yaw, rise, width)` makes 0.3 m treads of at most 0.28 m rise (not
  solid, so the support grid makes them walkable) with a landing on top. `Platform(parts, at, yaw, width, length,
  deckHeight, rails)` is a deck on posts with optional rails: the piece for jetties and docks.
- **From parts to a model.** `FloraForm.KitBuilding` and `FloraForm.KitRuin` call `FloraGenerator.KitStructure`,
  which turns each part into a box (Moss as foliage, the rest as bark; trims and moss drop at LOD2), and adds
  colliders: intact buildings get one circle per solid part up to the eaves (wall circles set back so their outer
  edge is the wall's outer face, W-58b) plus one fat cylinder inside; ruins get a circle per standing ground-storey
  piece. Kit forms are Founded, so they get a support grid of walkable tops (door steps, stair treads, landings).
  Species fields: `Height` x `Spread` is the width, `KitCulture` the trim; ruins take their wall colour from `Bark`
  and moss from `Accent`.
- **How a layout uses it.** Settlement plots name species. `FloraPlacement.PlotSpecies()` swaps the biggest
  settlement's house, seat, inn, smithy, store and workshop plots to the `KitHouse` species (FloraForm.KitBuilding),
  which stands on the plot centre facing `SettlementPlot.Facing`; `FloraGenerator.DoorFront` gives the door step
  distance. Ruin landmarks use a species with `FloraForm.KitRuin`.
- **Walk test (W-58b).** Intact doors are closed (no interiors yet): the player walks up to all 5 tested ElfGrove
  doors and stops on the step. 7 of 24 KitHouse variants have an outside stair; all climb to the 3.4 m landing.
- **For fishing villages (W-59).** Add a coastal trim (a new `FolkCulture` case or a variant of Human), build huts
  with `House` (1 storey, small bays), docks and jetties with `Platform` (deckHeight above the water level,
  rails), and boathouses as `House` on a `Platform`. Give them their own species with `KitCulture` set, and a
  settlement layout whose plots name those species. Keep new random draws after the existing ones in `House`, or
  every existing kit building changes.
