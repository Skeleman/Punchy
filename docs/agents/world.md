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
  At entrances the terrain stores each vertex's distance to cave air (Morph.w) and its shader cuts the surface
  just inside the cave's edge, in every pass (near LODs only); `CaveSystem` builds the cave walls with surface
  nets in background jobs, sets `CaveSystem.Underground` (weather stops) and lights a lantern on the player.
- **Plants, animals and particles are not baked:** editing their assets in Play mode applies live within about a
  second (the systems hash their config and rebuild).
- **Ponds are pruned after baking:** `BuildPlaces` labels wet cells on a 16 m grid and bakes `PondMask`, which
  zeroes every pond under `PondMinCells` (3) cells. Landmarks are placed before ponds, so this never moves them;
  a new pond rule goes through the mask, not more erosion or threshold tuning.
  With a biome map (island v2) the core test uses the map's sharp weights: the blurred shape weights never pass
  0.4 in its small swamp patches, which left v2 with no ponds at all.
