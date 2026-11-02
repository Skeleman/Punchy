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
  Ponds above sea level are stepped pools (W-24, `TerrainSource.Terrace`): the bed snaps to 0.6 m height bands,
  each band a level pool behind a low bank, so hillside swamps hold water instead of dry pits. The 16 m pond
  grid labels on the pre-terrace dig (`PondDig`), or the banks split every pond and the mask drops them.
- Roads (W-16): the A* routes run in parallel on a managed copy of the cost grid with a precomputed wander array; Emit and bridge sampling run in parallel and are added serially in pair order, so output is identical. The `[bake] stages` line splits settlements into towns/roads/roadside/blob. Settlements went from about 970 ms to about 290 ms (editor bench, hash unchanged).

## Island version and waterfalls (W-22)

- v2 is the default island: no flag runs v2 (landmarks 125 / AFA9CC63, cave hash D531293D622E785A since the W-21
  biome-blend fix). `-island v1` or PUNCHY_ISLAND=v1 runs v1 (101 / E128CB1D). The island isn't synced over the
  network: host and client each read their own launch, so start both the same way.
- BiomeData.SampleMapBlurred sums every biome id's weight and keeps the top 4 minus the 5th. Keeping only the first 4
  ids it met made weights jump where a fifth biome came in, which raised vertical walls and pits.
- Above a fall's lip the lower pool's wide channel reaches back under the upper river (river cells are 16 m, channels
  up to ~50 m), so the lowest-carve rule picked the lower water and land showed between the surfaces.
  RiverQuery.FallWater keeps the upper surface up to the lip.
- Surface falls run their length in half the effect's lifetime (LandmarkWaterfall.shader); cave falls keep 1.62.
  LandmarkEffectSystem.FallSounds plays WaterfallRush loops at the 4 nearest falls, logged as `[falls] sound sources`.
