# Systems map

Where each system lives under Assets/Game/Scripts, and the in-game debug keys.

- `Terrain/` — baking, chunk streaming with geomorphing LODs, rivers (priority-flood drainage), biomes, surfaces.
- `Weather/` — weather is anchored in the world: drifting cells over a pressure field, rendered into world maps
  (weather, wetness, cloud) that every shader reads. Volumetric clouds, lightning and synthesised thunder.
- `Flora/` — `FloraSpecies` assets (form, size, colours, finish, placement rules); `FloraSystem` places plants on
  per-layer jittered grids in background Burst jobs and draws them with GPU culling and indirect draws.
- `Ambience/` — heat haze, breath and snow puffs, fireflies and butterflies, shared `AmbientParticleType`
  assets (sparkles, spores, embers, ash, fog wisps) that biomes list, and `LandmarkEffectSystem` (landmark plumes
  and waterfalls, animated entirely in the shaders).
- `Terrain/Landmarks/` — landmark assets, placement and terrain stamping. Landmark props are drawn by the flora
  system (rock forms `Boulder`, `Spire`, `Crystal`, `Den` are flora forms); residents are spawned by
  `FaunaDirector` when the player comes near.
- `Debugging/DebugControls` — in-game keys: N / Shift+N jump between auto-found places (one per biome, summit,
  lake, river, beach, one of each landmark type), B day type, Y +2 h, T fast time, ] / [ force / clear weather, F1 hide the panel.
