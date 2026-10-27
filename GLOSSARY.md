# Punchy glossary

One word per idea, used the same way in code, assets, commit messages, docs and messages between sessions. Terms in
`code` are existing types or fields. **(new)** marks a term for planned work; the session that builds the feature makes
it real. To add or change a term, edit this file in the same commit as the code that uses it.

## World and ground

- **Seed**: the number everything procedural derives from. Same seed, same island; nothing derived from it needs
  network sync.
- **Island**: the whole generated world.
- **Ground truth**: `TerrainSource`. To *sample* is to ask it about a point (`SampleGround`, `SampleHeight`,
  `FloorUnder`, `SampleBiomes`). Nothing computes height any other way.
- **Bake**: build once from authoring data into a blob (terrain, rivers, landmarks, caves, layouts). Editing baked
  assets re-bakes. **Live**: rebuilt at runtime when its asset changes (plants, creatures, particles).
- **Chunk**: a fixed-size terrain or cave streaming unit, built and dropped by distance. **Tile**: the same idea for
  drawn scatter (plants, critters, ground detail, ambient particles): a square of a layer's grid streamed around the
  camera. **Cell**: one square of a grid: a plant slot in a flora layer (at most one plant), a bucket of the brain's
  16 m neighbour grid, or a square of a baked spatial index. Don't use these words interchangeably.
- **Budget**: the per-frame cap on streamed or background work (`CellsPerFrame`, chunks per frame).
- **Biome**: a `BiomeDefinition` asset. **Region**: one warped Voronoi cell, which picks a biome from its **Climate**
  (altitude, temperature, humidity, volcanism). **Surface**: ground material (grass, snow, rock…).
- **Glacier** (new): the ice biome that hosts ice caves.

## Places

- **Landmark**: a small placed place (`LandmarkType`): a **Stamp** (terrain shape) plus contents: pools, props,
  effects and residents. Biomes list landmarks with a density per km².
- **Settlement**: a landmark where a folk lives (HumanVillage, DwarfHold, ElfGrove, GoblinCamp, OgreCamp; the imps'
  **Warren** (new)). "Town" in conversation means settlement.
- **Layout** (new): `SettlementLayout`, a settlement's pattern of **Square**, **Streets** and **Plots**, baked into a
  `SettlementBlob`.
- **Road** (new): a path between settlements and cave mouths. A **Street** is a path inside a settlement.
- **Seat** (new): the leader's main building. **Shrine** (new): any religious building, from a roadside idol to a
  temple. **Water feature** (new): a built pool, well, fountain or channel inside a settlement.

## Things the flora system draws

- **Form**: a procedural shape generator in `FloraGenerator` (`FloraForm`: BroadleafTree, Conifer, Palm, Boulder,
  Hut…). A `FloraSpecies` picks a form; each species has a few **Variants** (models).
- **Plant**: living flora. **Tree**: a BroadleafTree, Conifer or Palm (`TreeKind`): the forms animals climb, perch
  in and hang webs from. **Rock**: a Boulder, Spire or Crystal form.
- **Structure**: any built form (Hut, StoneHouse, Tower, Tent, walls, fences, stalls, signs). **Building**: a
  structure someone lives or works in; its size is set by its folk's scale.
- **Prop**: anything a landmark or plot places on purpose (`LandmarkProp`), as opposed to biome-scattered plants.
- **Placement**: `FloraPlacement`, the pure function from a cell to the plant in it, shared by drawing
  (`FloraSystem`) and animals (`FloraLookup` in the simulation world), so both agree on every tree.

## Caves

- **Cave**: one cave landmark's underground space. **Network** (new): a cave with several chambers and branches.
- **Mouth**: where a cave meets the surface (`IsCaveHole`). Kinds: cliff mouth, **Sinkhole**, **Shaft**.
- **Passage**: a tunnel segment. **Chamber**: a room.
- **Air** and **Rock**: inside and outside the cave's signed distance (`CaveDistance`, `IsRock`).
- **Underground**: the camera is inside cave air (`CaveSystem.Underground`).
- **CaveStyle** (new): an asset for a cave's look and shape (limestone, lava tube, ice, crystal, metallic, flooded,
  mine…). **Speleothem** (new): stalactites and stalagmites.
- **Glow**: emissive light from flora, fauna, water or lava. One shared term, whoever owns the emitter.

## Creatures

- **Creature**: any ECS body built from parts: animals, folk and the player's avatar.
- **Species**: `CreatureSpecies`. A **Variant** is a species that borrows another's body (`VariantOf`: arctic fox
  from red fox); a **Coat** is a weighted colouring.
- **Part**: a `LimbSlot` on a species, holding a **Limb** (`LimbType`: a chain of **Segments**, each a tapered
  capsule) with a **Role** (`LimbRole`: Leg, Arm, Tail, Neck, Head, Ear, Horn, Eye, Feature, Wing). **Key poses**:
  a limb's Idle, Walk and Run reach/push/lift angles; the **Gait** blends them by **Phase**, and the **Stride** is
  measured from them so feet don't slide.
- **Individual**: `CreatureState.Individual`, fixed at spawn. It drives **Traits** (gait quirks, groom side…),
  **Style** options (hairstyles, beards) and hair colour.
- **Folk**: the humanoid species (Human, HumanChild, Dwarf, Elf, Goblin, Ogre, Imp (new)). **Animal**: any creature
  that isn't folk.
- **Avatar**: the creature the player drives (`PlayerAvatar`). **Remote avatar**: another player's (`RemoteAvatar`).
- **Habitat**: how it moves through the world, `Habitat`: `Land`, `Swims`, `Floats`, `Flies`. Where it lives is a
  **Lair**, not a habitat: a burrower is a Land animal with a burrow; a bat is a Flies animal with a roost.
- **Wild**: spawned by the **Director** (`FaunaDirector`) by biome density; disposable, regenerated rather than
  saved. **Resident**: bound to a landmark (`LandmarkResident`) with a **Home** and a **Leash**. **Worker**: a resident
  with a **Task** (`CreatureTask`: Hammer, Chop, Gesture, Tend, Watch) done at its **Work spot**. **Domestic** (new):
  an animal resident of a settlement (dog, cat, chickens, rooftop birds).
- **Brain**: `CreatureBrainSystem`, running only in the **simulation world**. Between activities it **Decides**: picks
  a **Mode** (`CreatureMode`: Idle, Wander, Flee, Graze, Drink, Rest, Sleep, Play, Hunt, Work, Climb, Web, plus Puppet
  for tests) weighted by the species' **Tastes** (Grazing, Thirst, Playfulness, Laziness, Hunger, Climbing…).
  A **Stage** is a step within a mode (walk to the tree, climb, sit…).
- **Posture**: a blended whole-body pose eased by the brain and replicated (`Lie`, `HeadDown`, `Bow`, `Curl`, `Sit`,
  `Vertical`, `Fly`). **Idle action**: one of a species' **Habits** (`IdleAction`: PawGround, Groom, Scratch, Shake,
  Stretch, Sniff, Sit, SitUp, TailFlick, Preen, HeadBob, LookAround, Howl), done while calm.
- **Flee**, **Alarm** (one of a herd bolting sets off the rest), **Flock** (birds following one that takes off),
  **Dive** (a bird of prey's hunt on fish), **Caught** (`CreatureState.Gone`: removed by the Director).
- **Need** (new): a drive that rises over time (hunger, thirst, rest, safety, social). Needs give animals agency
  instead of wandering.
- **Lair** (new): an animal's home: nest, den, burrow, roost, web. Seed-derived lairs persist (cheap to regenerate);
  dynamic ones are capped.
- **Trace** (new): something a creature leaves behind that fades and is capped: footprints, feathers, shells,
  droppings. Drawn per machine, never synced.
- **Interaction** (new): a folk or player action on an animal: **Pet**, **Hunt**, **Carry**. (An animal hunting is
  `CreatureMode.Hunt`; the same word, the same idea.)

## Hand-off between places and creatures

- **Anchor**: `Punchy.Terrain.Anchor`, a point that world geometry exports for creatures to use; baked from the seed
  into `TerrainSource.Anchors`, so nothing to sync, and immutable (users count occupancy). Fields: Position (on the
  surface used: a roost's ceiling point, a perch's top), Facing, Size (radius of the spot), Owner (cave or
  settlement), Kind, Capacity (0 = unlimited), Biome, Tags (Shelter bit, plus the cave style or settlement folk).
  Kinds (`AnchorKind`, a byte, append only): Door, Work, Stall, Well, Bench, Gate, Altar, Seat, Roost, Perch, Nest,
  Burrow, Pool, Hearth, Yard, Glow (a glowing plant: creatures drawn to light gather there). Look up with `Anchors.Near` and `Anchors.Pick` (Burst-safe, allocation-free). The world
  session creates anchors; the animal session decides how creatures use them. This is the only contract between the
  two. (`CreatureState.Anchor` is the point a creature is using right now: a world anchor, or one it found itself: a
  tree's foot, its web's centre, its burrow or nest.)
- **Nest**: where a bird sleeps (`CreatureSpecies.Nests`: Branch, a twig cup against a trunk; Treetop, a stick
  platform; Ground, a grass scrape). Picked from its 16 m lair cell, like a burrow, so a bird that despawns and comes
  back has the same nest with nothing saved. Not the same as the Nest anchor kind (a cave ledge), which birds don't use yet.

## Multiplayer and testing

- **Solo**, **Host**, **Client**: the three ways to run (`GameBootstrap.Solo`). Solo has no netcode at all. The
  **simulation world** is where brains run (`NetWorlds.Server`: the server, or the local world when solo).
- **Ghost**: a replicated entity. **Replica**: a client's rebuilt copy of creature state (`CreatureReplica`).
- **Shared clock**: `_PunchyClock`, the server tick in seconds; ambient motion runs on it so screens agree.
- **Status line**: the timestamped "Net status" log line every 10 s. **Test flags**: `-place`, `-time`, `-autowalk`,
  `-watch`, `-spawn`, `-pose` (one specimen held in daylight while the camera circles it, e.g. `Bat:glide:70`),
  `-incave`, `-at <AnchorKind>` (beside the nearest anchor of that kind, e.g. `-at Pool`), `-watchpitch`, `-timescale`,
  `-nohud`, `-host`, `-join`, `-hostonline`, `-joincode`, `-body`, `-weatherseed`. In the
  editor, Play mode also reads the DebugControls flags from `Temp/PlayArgs.txt`, so tests need no build.

## Sessions

- **Animal session** ("animal behaviour manager", claude-41 [635c9b], this PC): branch `animal-behaviours`. Owns
  `Fauna/**`, the brain, creature assets, FloraPlacement.
- **World session** ("world feature manager", formerly claude-01): branch `world-features`, worktree
  D:\Unity\Punchy-world. Owns caves, landmarks, settlements and their geometry. See WORLD.md.
- **Perf session** (B: "windows-7l945c9-kind-possum", the other, weaker PC): profiling and performance; currently
  the weather haze (`Weather/`).
- **Supervisor** (claude-41 [21fe18]): coordinates the sessions and relays the user's direction.
