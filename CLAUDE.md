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
  surface asset re-bakes the terrain, rivers included, which takes a while and can reshuffle the island.
- **Biome placement:** regions are warped Voronoi cells. Each cell reads a climate from smooth fields
  (`BiomeSampling.Climate`: altitude, temperature falling with altitude, humidity, and volcanism around a few
  hotspots in high country) and picks a biome weighted by `Frequency` × how well the climate fits the biome's
  Climate ranges, so arid biomes cluster, wet ones cluster, and each volcano sits inside ashlands. The region at
  the origin is always the first biome. Not every seed gets every biome; to force a layout, export the biome map
  (TerrainAuthoring inspector), paint it, and set Biome Source to Map.
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

## Systems (Assets/Game/Scripts)

- `Terrain/` — baking, chunk streaming with geomorphing LODs, rivers (priority-flood drainage), biomes, surfaces.
- `Weather/` — weather is anchored in the world: drifting cells over a pressure field, rendered into world maps
  (weather, wetness, cloud) that every shader reads. Volumetric clouds, lightning and synthesised thunder.
- `Flora/` — `FloraSpecies` assets (form, size, colours, finish, placement rules); `FloraSystem` places plants on
  per-layer jittered grids in background Burst jobs and draws them with GPU culling and indirect draws.
- `Fauna/` — `LimbType` (segments plus seven key poses), `CreatureSpecies` (body plus slots; can be a recoloured
  variant of another species, with weighted coats). ECS entities; `CreatureBrainSystem` (behaviour) and
  `CreatureRenderSystem` (posing, one instanced draw) are Burst jobs. `FaunaDirector` spawns per biome density.
  Behaviour: each species' Activities (active by day/night, grazing, thirst, playfulness, laziness, what it hunts)
  weight what it does next: graze, drink at the water's edge, rest, sleep, play chase with its own kind, hunt;
  prey flee predators. Animals see each other through a per-frame snapshot grid. Activities set posture weights
  (lie, head down, play bow, curl) that `CreatureAnimator` applies procedurally, so no extra poses are authored.
- **The player's body is a creature.** `FaunaDirector.PlayerSpecies` (a `CreatureSpecies`, currently `Human`)
  is spawned as a creature tagged `PlayerAvatar`; `PlayerAvatarSystem` copies the player's position, heading and
  speed into it instead of a brain, so any species can be played and moves with its own gait. Setting the field
  to empty falls back to the player's placeholder mesh. Humanoid folk (Human, Dwarf, Elf, Goblin, Ogre) share the
  Human limb parts with different slot scales and thicknesses, plus their own torso or leg poses where the gait
  differs (goblin crouch, ogre sway). Settlements are landmarks, one per culture (HumanVillage, DwarfHold,
  ElfGrove, GoblinCamp, OgreCamp): mostly residents of that folk, in its own buildings (flora forms Hut,
  StoneHouse, Tower, Tent, turned to face the square). Residents get a Home and Leash so they stay in town.
  Set pieces use placed props (LandmarkProp.Count/Angle/Clearance: exactly N round a circle, everything else kept
  clear): an inn and smithy in human villages, a smithy in dwarf holds, pens and stalls in camps, a forum and
  fountain at the heart of elf groves. Residents with a Leash and HomeRing/HomeAngle live at such a spot (penned
  animals); nothing with a home heads out of its range, even fleeing.
- Rocks (Boulder form) come in five families by variant (lump, slab, L, standing, monstrous) and carry their own
  collision: FloraModel.Colliders, a few upright cylinders hugging the footprint, turned and scaled with each
  placed rock (other solid plants use one trunk cylinder). FloraSpecies.SizeRange sets each placed plant's
  random size; a landmark prop's Scale multiplies it.
- **Individuals:** CreatureState.Individual is fixed at spawn and drives CreatureBlob.Trait (posture lean, arm
  swing, bounce, torso twist, head tilt, pace), hair colour (segments marked Hair, from the species' HairColors)
  and style options (slots with the same StyleGroup: each individual shows one; list a slot twice to make it
  commoner). The humanoids' slot 3 is a placeholder kept so later slot indices don't shift.
- **Workers:** a LandmarkResident with a Task (Hammer, Chop, Gesture, Tend, Watch) lives at its HomeRing/HomeAngle
  spot facing FaceRing/FaceAngle, works by day in CreatureMode.Work (arm poses in CreatureAnimator.TaskPose),
  takes breaks within 7 m, and dozes at its post at night. LandmarkResident.Size overrides size (HumanChild is a
  small, playful Human variant).
- Flora tubes' caps are cones whose height scales with the radius: don't cap wide tubes (rims, platforms). Slot `Parent` indices count slots in the species' list,
  not the mirrored copies.
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

- **Every opaque custom shader needs a `DepthNormals` pass** (as well as `DepthOnly` and `ShadowCaster`). SSAO
  uses depth + normals, so URP builds the camera depth texture from the depth-normals prepass; anything without
  the pass is missing from it, and effects that read depth (heat haze, soft particles, SSAO) treat it as the
  ground behind.
- **Never `Shader.Find` at runtime.** Builds strip shaders nothing references; reference shaders or materials from
  a serialized field (see `WeatherSystem.PrecipitationShader`).
- **`Mathf.SmoothStep(a, b, t)` is an interpolation, not shader smoothstep.** Use `FloraMeshBuilder.Smooth` or
  `math.smoothstep`.
- Large world coordinates lose float precision in noise: keep noise offsets small and use integer hashes for
  per-cell randomness (see `PunchyCellHash` in `Sky.hlsl`).
- Heavy background jobs crowd out the terrain's own jobs and cause hitches; keep per-frame placement budgets small
  (`FloraSystem.CellsPerFrame`).
- `TerrainSource` sampling is the hot path: terrain meshing samples it about six times per vertex, and plants,
  animals and ambience call it constantly (some from plain C#, not Burst). Anything that's slow to work out per
  region belongs in a baked table (see `BiomeCellTableBlob`), not in the per-sample code.
- Never `Schedule(...).Complete()` heavy work in the same frame: the main thread ends up doing it. Pick results up
  a frame or two later, as `TerrainStreamingSystem` and `FloraSystem` do.
- Editor entity names are limited to a few thousand distinct strings; don't give streamed entities unique names.
- Serialized values in scenes and assets override code defaults: changing a default in code doesn't change
  existing assets.
- Two-legged species set **Upright** so the body stays level on slopes (the legs take up the ground); others tilt
  with it. No body tilts to follow the ground while airborne.
- Species have a **Habitat**: Land, Swims (fish, eels, dolphins) or Floats (ducks).
  - Swimmers keep to water at least 0.7 m deep. They swim `clamp(depth × 0.45, 0.25, SwimDepth)` under the
    surface and leap out `Leaps` times a minute. `CreatureState.Air` runs 0..1 through a leap.
  - Floaters ride on the surface.
  - The director spawns water species only at water spots, and land species only on dry ones.
  - Aquatic bodies are legless, so `LeglessStride` sets their gait. The animator treats `Position.y` as the
    body's centre, with no ground tilt.
- Flora with **Aquatic** grows only where the water depth is within `WaterDepth`. **Floats** puts it on the
  surface (the LilyPad form); otherwise it rises from the bed (Reeds). Floating plants avoid currents.
  `MinRapids` limits a species to white water (RiverRock).
- **Water.shader** draws three liquids from per-vertex data. Look `a` is murk, and from about 0.55 up it turns
  thick and oily (slow swell, bubbles, scum). The glow channel is `float2` (toxic glow, lava). Lava amount comes
  from `WaterLava` on biomes and landmarks (VolcanoCone).
  - Reflections march screen space through the depth texture and sample the opaque texture, so the RP assets
    must keep Opaque Texture on.
  - Inside the march, sample with explicit LOD: implicit derivatives in a loop give a compile warning.
- Reimporting an asset during play mode (e.g. `AssetDatabase.ImportAsset` on a shader) rebakes the subscene and
  sends the player back to Start. Probe scripts should edit shaders while the editor is stopped.
- Swimmers in rivers drift downstream at `PlayerMotor.RiverCurrent`, doubled in rapids. The drift is capped
  below swim speed, so the player can always reach the bank.
- Creature parts are drawn solid now (the capsule mesh was once wound inside-out, which let hidden parts show
  through), so a part meant to be seen must poke out of whatever it's attached to.
- Joint axes depend on which way a part points: for a part pointing up (an upright torso, neck or head), a yaw
  leans it sideways and a roll (z) twists it about its length.
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
