# Systems audit (2026-11-06)

Goal: find one-off code that should become shared systems before the roadmap (game overview doc) builds on it.
Scope: origin/main, Assets/Game/Scripts. Severity: **H** = blocks a roadmap item or will be copied again soon; **M** = costs time each new feature; **L** = tidy-up.

## What's already in good shape
- ECS/Burst core: terrain chunks, flora, creatures, brains and render all run as jobs.
- Data assets already exist for biomes (BiomeDefinition), fauna species, critter and domestic tables and surfaces.
- The dungeon spawner already has the right shape: socket tags plus rows. The other spawners should copy it.
- Landmarks use stable per-type salts, so new types don't move old ones.

## Findings

| # | Sev | Finding | Why it matters | Fix | Lane |
|---|---|---|---|---|---|
| 1 | H | **Six-plus spawn paths:** FaunaDirector has SpawnWildFolk, SpawnCrossingFishers, SpawnResidents, SpawnHoneyThief, SpawnMessenger, SpawnTraveller, SpawnCritters and SpawnDomestics. CritterTable, DomesticTable, DungeonSpawner, AmbientLife and CaveFolk each pick on their own. | Roadmap wants tables per biome, chamber, time and world state, plus spawns decoupled from the seed. Each path would need that added separately. | One **SpawnTable + SpawnSocket** system shaped like DungeonSpawner. Rows filter on biome, biome family, chamber type, time, season and tags, and say whether the pick is seeded or live. The existing paths become table rows. | Animal (A card), after A-79 |
| 2 | H | **Colossi are hard-coded:** StoneColossi.cs (153 KB) builds each colossus in its own method. Biomes, Salts, KeepRadius and Casts are parallel arrays indexed by slot, which is fragile (Casts already has an extra "last entry" special case). ColossusRigs has a base class, but each rig hand-codes its geometry. | Roadmap: several colossi per biome, one primary. Each new colossus touches about five arrays plus a new method. | A **ColossusDefinition** asset (biomes, salt, keep radius, cast, rig type, primary flag) plus a placement pass that reads a list. Geometry builders stay in code for now, one file per colossus. | Animal |
| 3 | H | **No shared stats/actor model:** creatures, folk and the player each keep their own health, speed and comfort. | Combat, taming/riding thresholds, skills-by-use and quests all need one. | Design note first: an **Actor stats** component (vitals, attributes, skills, faction and disposition) shared by player, folk and creatures. Then a small first card. | Supervisor writes the note; then player + animal |
| 4 | H | **Climate is split in three:** WeatherField, CreatureComfort and WeatherTalk each work out temperature and wetness their own way. | Roadmap: climate from biome, elevation, time and season drives weather, creatures, NPC talk and flora. | One **ClimateField** sample (temperature, humidity, air quality, wind, season) that the other three read. | World owns the field; animal switches its readers |
| 5 | M | **Races and biomes as names:** the ComfortRace enum (6 files) and biome-name string literals (IslandV2, StoneColossi, GrandFalls, Iceberg, Mantrap, ForestFire). | New races and sub-biomes mean editing code in many places. Typos fail silently. | **Tags on the data assets** (biome family: Wet/Hot/Cold…; race traits) and look-ups by tag. Rule: no new biome-name literals in game code. | World (biomes), animal (races) |
| 6 | M | **One settlement baker per race:** igloo, elf, fishing, desert and human towns each place their own way. | Roadmap: unique kits with interiors and doors; fishing is a trait, not a race. | **One settlement planner** (plots, roads, squares) + per-race **kit** data + **traits** (fishing → wharf, piers). Ties in with P-lane interiors and doors. | World, after W-115 |
| 7 | M | **Hand-editable world is half done:** HeightmapIO and BiomeMapAsset exist, but borders, rivers and landmark overrides can't be edited by hand. | Zach's vision: hand-editable external files with landmark overrides. | Extend to one **world override file** (JSON) for landmark add/move/remove, river paths and biome patches, applied after generation. | World |
| 8 | M | **Giant files:** DebugControls 335 KB, FloraGenerator 320 KB, CreatureRenderSystem 188 KB, StructureKit 132 KB, FaunaDirector 116 KB, CaveSystem 115 KB, CreatureBrainSystem 112 KB. | Slow to change, merge conflicts between lanes, and agents burn tokens reading them. | Split along partial-class lines whenever a card touches one: DebugControls by area, FloraGenerator per plant family, CreatureRender per body plan. No standalone refactor cards. | Each owning lane, as they go |
| 9 | M | **Caves and ruins separate:** CaveSystem and the temple kit each carve and fill on their own. | Roadmap: caves and ruins interconnect, sharing chambers, sockets and tables. | Already started with W-115 (shared sockets, chamber kit). Keep the socket and chamber contract in one place (DungeonSocket). | World (W-115), animal (tables) |
| 10 | L | **Duplicate enum names:** Family, Kind and Ridge each exist in more than one namespace. | Confusing; wrong-type bugs. | Rename when touched (BiomeFamily, ColossusKind…). | Any |
| 11 | L | **Perf unknowns:** many systems (ambient life, critters, colossi, far shadows) haven't been measured together on v3. | "Optimized for what it needs to do" needs numbers. | Profiler: one v3 sweep (ms per system, GC, draw calls) at 3 spots; flag anything over 0.5 ms. | Profiler |

## Order
1. Profiler sweep (11) runs now, in parallel.
2. Animal: spawn tables (1), then colossus definitions (2).
3. World: climate field (4) and biome tags (5) after W-115. Settlement planner (6) after that.
4. Supervisor: actor stats design note (3) and override file design note (7), both for Zach to approve before any cards.
5. Splits (8) and renames (10) happen when cards touch those files.

Rule added to cards from now on: **no new biome or race name literals in game code, and no new standalone spawn path.** Add a table row or tag instead.
