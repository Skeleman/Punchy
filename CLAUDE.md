# Punchy

An open-world exploration prototype: a procedurally generated island with biomes, rivers, weather, a day/night
cycle, procedural plants and procedurally animated animals. Unity 6000.5.8f1, URP 17.5, Entities 6.5 (ECS + Burst),
Input System. Windows is the only build target so far.

## How the world fits together

- **`TerrainSource` is the single source of truth** for the ground: height, biome weights, rivers, lakes, water
  and surface material at any point (`SampleGround`, `SampleHeight`, `SampleSurface`, `SampleNormal`). Terrain
  meshes, player movement, plants, animals, weather and particles all sample it, so they always agree. Don't
  compute heights any other way.
- It is **baked** from `TerrainAuthoring` in the `Assets/Game/Scenes/World.unity` subscene. Editing a biome or
  surface asset re-bakes the terrain, rivers included, which takes a while and can reshuffle the island
  (biome regions come from Voronoi cells weighted by each biome's `Frequency`; the region at the origin is always
  the first biome).
- **Data-driven assets, not code, define content.** Biomes (`Assets/Game/Biomes`) list their surfaces, terrain shape,
  water look, weather odds, ambience, flora, fauna and ambient particles. Most new content is a new asset or a
  biome list entry.
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
- **Plants, animals and particles are not baked:** editing their assets in Play mode applies live within about a
  second (the systems hash their config and rebuild).

## Systems (Assets/Game/Scripts)

- `Terrain/` — baking, chunk streaming with geomorphing LODs, rivers (priority-flood drainage), biomes, surfaces.
- `Weather/` — weather is anchored in the world: drifting cells over a pressure field, rendered into world maps
  (weather, wetness, cloud) that every shader reads. Volumetric clouds, lightning and synthesised thunder.
- `Flora/` — `FloraSpecies` assets (form, size, colours, finish, placement rules); `FloraSystem` places plants on
  per-layer jittered grids in background Burst jobs and draws them with GPU culling and indirect draws.
- `Fauna/` — `LimbType` (segments plus seven key poses), `CreatureSpecies` (body plus slots; can be a recoloured
  variant of another species, with weighted coats). ECS entities; `CreatureBrainSystem` (behaviour) and
  `CreatureRenderSystem` (posing, one instanced draw) are Burst jobs. `FaunaDirector` spawns per biome density.
- `Ambience/` — heat haze, breath and snow puffs, fireflies and butterflies, shared `AmbientParticleType`
  assets (sparkles, spores, embers, ash, fog wisps) that biomes list, and `LandmarkEffectSystem` (landmark plumes
  and waterfalls, animated entirely in the shaders).
- `Terrain/Landmarks/` — landmark assets, placement and terrain stamping. Landmark props are drawn by the flora
  system (rock forms `Boulder`, `Spire`, `Crystal`, `Den` are flora forms); residents are spawned by
  `FaunaDirector` when the player comes near.
- `Debugging/DebugControls` — in-game keys: N / Shift+N jump between auto-found places (one per biome, summit,
  lake, river, beach, one of each landmark type), B day type, Y +2 h, T fast time, ] / [ force / clear weather, F1 hide the panel.

## Animation conventions (Fauna)

- A part's segments point along their local +Z. A slot's `Direction` is a rest rotation in degrees (Unity Euler,
  ZXY): `(90, 0, 0)` points down (legs), `(0, 180, 0)` backwards (tails), `(0, 90, 0)` out to the right.
- Joint angles are relative to the previous segment. **Positive pitch swings a leg backwards and droops a tail.**
  Mirrored slots flip yaw and roll.
- A gait cycle is three keys: stance sweeps linearly reach → push (foot planted), swing eases push → reach plus
  `lift × sin`. Gaits come from slot phases (walk 0/¼/½/¾, trot = diagonal pairs, hop = pairs together).
- Stride and standing height are measured from the key poses, so feet don't slide; change poses rather than
  speeds to fix sliding.
- Body sockets are in body units: x and y across the radius, z from −1 (rump) to 1 (chest). Child sockets are
  metres from the **end** of the parent's last segment, in that segment's frame.

## Gotchas

- **Never `Shader.Find` at runtime.** Builds strip shaders nothing references; reference shaders or materials from
  a serialized field (see `WeatherSystem.PrecipitationShader`).
- **`Mathf.SmoothStep(a, b, t)` is an interpolation, not shader smoothstep.** Use `FloraMeshBuilder.Smooth` or
  `math.smoothstep`.
- Large world coordinates lose float precision in noise: keep noise offsets small and use integer hashes for
  per-cell randomness (see `PunchyCellHash` in `Sky.hlsl`).
- Heavy background jobs crowd out the terrain's own jobs and cause hitches; keep per-frame placement budgets small
  (`FloraSystem.CellsPerFrame`).
- Serialized values in scenes and assets override code defaults: changing a default in code doesn't change
  existing assets.
- Joint angles accumulate along chains: a snake or tail ripple needs small per-joint angles (about 10–15°).
- `MaterialPropertyBlock.SetInt` stores a float; use `SetInteger` for `int`/`uint` shader properties.
- Editing a landmark type re-bakes the terrain (its shape is baked), unlike plant and animal assets.

## Building and releasing

- Builds go to `Builds/Windows/Punchy.exe` (git-ignored); build with `BuildPipeline.BuildPlayer` on the scenes in
  Build Settings for `StandaloneWindows64`.
- Releases are GitHub releases with the zipped build attached (leave out
  `Punchy_BurstDebugInformation_DoNotShip`). The repo is private, so only collaborators can download them.
- After a build, Unity rewrites some settings files (URP shader prefiltering, preloaded input assets); check
  `git status` before committing so unrelated changes don't ride along.

## Working in this repo

- Keep code commented the way it is: a summary comment on each class saying what it is and why, short comments on
  non-obvious steps. Match the surrounding naming and idioms.
- Collaborators: work on a branch or fork rather than committing to `main` directly.
