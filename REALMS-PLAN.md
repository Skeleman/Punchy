## Realms plan (J-026), world session draft for the supervisor and kind-possum's cost review

### 1. How realms work

- **Realm asset (new, `Assets/Game/Realms`).** Fields:
  - sub-biomes: BiomeDefinitions, each with a weight;
  - climate centre: temperature, humidity, altitude, volcanism;
  - allowed neighbours;
  - transition width: soft inside the realm, hard across realms;
  - habitability and allowed cultures;
  - a cave-style override list.
  - The 14 current biome assets stay as they are, with their landmarks, surfaces and caves.
- **Placement, two levels.**
  - Realm regions: large warped Voronoi cells, about 4× today's biome cells. Each picks the realm whose climate centre fits best, as biomes do now.
  - Sub-biomes: picked inside each realm region on today's finer cells, by the existing Climate fit × Frequency, but only from that realm's list.
  - Today's BiomeSampling stays the inner level, with an outer realm lookup in front of it. Both are baked into BiomeCellTableBlob, so per-sample cost stays 9 cells and 4 weights.
- **Adjacency.**
  - After picking realm regions, a fix-up pass swaps any pair that isn't allowed: Frost–Fire, Frost–Desert and Fire–Wetlands become a Temperate or Mountains buffer cell between them. Deterministic, in seed order.
  - Flora across a realm border gets a hard edge: a species only spawns where its own realm's weight is above 0.5. Inside a realm, flora blends as now. This replaces the separate flora-border fix (loan #10) and uses the same FloraPlacement change.
- **Habitability (J-027).** Two additive fields on BiomeDefinition:
  - `Habitability`: None / CampsOnly / Any;
  - `Cultures`: a flags enum (Human, Dwarf, Elf, Goblin, Ogre, Snow folk and Fish folk later).
  - SettlementBaking only places a settlement type where the biome under its centre allows that culture. Hermit spots ignore it.
- **Fingerprint and re-bake.** Accepted: the island reshuffles. Landmarks, caves, settlements, roads and anchors all re-bake from the same seed. A sweep afterwards confirms:
  - every realm has at least one settlement where its habitability allows one;
  - every listed landmark type is placed somewhere;
  - roads still route.
- **Preview.** Before any content: the export-biome-map button on TerrainAuthoring writes a PNG (realms tinted, sub-biomes shaded) for Zach to look at.

### 2. Realms and sub-biomes

New sub-biomes are marked NEW. "Reuse" means made from existing assets with tint and scale (J-007).

| Realm | Sub-biomes | Habitability |
|---|---|---|
| **Frost** | Glacier (high), Tundra, Mountains (snowy), NEW Crystal Spires | Tundra and Mountains: Any (Snow folk later). Glacier top and Crystal Spires: None. |
| **Fire** | Volcano (+ VolcanoCone), Ashlands, NEW Ember Forest, NEW Electric Wastelands | Ashlands: CampsOnly (Goblin, Ogre). The others: None. |
| **Desert** | Dunes, Sonoran, Salt Flats, NEW Mesa Canyonlands, NEW Glass Desert | Glass Desert: None. Mesa: Dwarf and Human (cliff dwellings later). The rest: Any. |
| **Wetlands** | Foggy Marsh, Poison Swamp, NEW Fungal Forest, NEW Jungle | Poison Swamp: None. Fungal Forest: Elf and Goblin. Jungle: Any. |
| **Temperate** | Grassland, Temperate forest, the Great Tree clearing (as a landmark), NEW Floating Isles | Floating Isles: None (a shrine is allowed). Grassland and forest: Human and Elf. |
| **Mystic** | SparklingForest, plus my addition NEW Moonglade, a twilight meadow lit by bioluminescent grass (glow flora that only lights at night) | Elf only. |
| **Coast** | a shore band round the island: beaches, sea cliffs, my addition NEW Tide Flats (wet mud, rock pools) | Human (coastal towns later, Fish folk later). |

Also proposed: **Bone Barrens** in Desert, white sand with giant fossil ribs and skulls. It reuses BoneGraveyard pieces at landmark scale, so it costs almost nothing.

### 3. New sub-biomes: content, with cost notes

| Sub-biome | Ground and flora | Landmarks | Cave style | Weather and particles | Cost |
|---|---|---|---|---|---|
| Crystal Spires | Ice and CrystalRock surfaces. Crystal forms at 6–20 m scale (Crystal rule, Scale up). | Walk-in geode (cave-stamp shape, small); frozen waterfall (Waterfall type with a frozen look); ice arches (from the polish queue) | Prismatic crystal ice: Ice style plus Facets and a tint | Snow, sparkles | Low: reuse plus scale. The geode is one new landmark. |
| Ember Forest | EmberCinder plus Ash. Reuse Broadleaf and Conifer with glowing bark (Finish Glowing on Bark, a small shader flag) and dead black trees. | Charcoal-kiln clearing (a dome plus smoke plume); ember tree (AncientTree, glowing) | Ember-root: Rock plus HangingRoots with glow | Firestorm preset: ember precipitation on the dry-precipitation path, orange sky, haze. Fire whirls: 2–3 spinning flame columns made of the new fire's cards. | Medium. The firestorm needs weather code: tell the Sun owner and kind-possum. Embers about 2k particles; whirls 3 draws. |
| Electric Wastelands | Obsidian and Basalt surfaces; obsidian spires (Spire form, dark, metallic) | Conductor ring (6–8 spires with constant arcs); magnetic anomaly (debris orbiting in a slow vortex); glass crater (crater stamp plus a glass surface) | Magnetite: Metallic faceted (caves round 2), floating pebbles, cyan seams | Arc flashes; sparks particle | **Arcs:** shader-only billboards between spire tips from a baked pair list, flickering on the clock; about 1 draw per 32 arcs. **Fallback:** static glow streaks. **Orbiting debris:** instanced rocks moved in the vertex shader, no collision. **Fallback:** static floating rocks. |
| Mesa Canyonlands | DesertRock with strata. Reuse Butte and HoodooField at larger scale. | Natural arch (reuse the ice-arch builder in sandstone); slot canyon (a narrow carved stamp); petroglyph wall (a decal on a cliff via the surface); cliff dwellings (settlement variant, later) | Sandstone alcoves | Dust | Low to medium. A slot canyon needs a new stamp shape; arches share code with the ice arches. |
| Glass Desert | Dunes terrain plus a glass surface (smooth, high gloss, slight translucency in the terrain shader); fulgurite spires (Spire, glassy) | Mirror basin (flat landmark, smoothness 1, so the existing reflections mirror the sky); sunken ruins (reuse W-24 ruins, half buried) | Glass caves: Crystal style, translucent | Heat haze, sparkle | Low. Reflections exist already. **Fallback:** if glossy terrain is costly at distance, fade gloss beyond 150 m. |
| Fungal Forest | Moss and loam. Giant mushrooms: Mushroom form at 5–15 m, glowing caps, as trees on the Tree layer. | Fairy ring (exists); spore geyser (reuse Geyser with a spore plume); mushroom-cap house (hermit spot) | Mycelium: Fungal plus glowing threads (HangingRoots with glow) | Spore haze, fireflies | Low: reuse plus scale. Giant mushrooms add about 3 models. |
| Jungle | Dense broadleaf (new tree variant species, 2–3), vines (a Hanging form on trees), ferns | Step-pyramid ruin (a stepped stamp plus a stone surface; reuses StoneHall parts); waterfall pool (exists); giant roots (reuse Roots at 10×) | Cenote: a sinkhole Grotto with a pool and an open shaft | Rain-heavy weather, mist | Medium: the most new flora. Draw risk: dense canopy. **Fallback:** lower density, bigger trees. |
| Floating Isles | Grassland ground below. Isles: inverted rock cones with a meadow cap, drawn as flora-path Boulder or Den-style models on the Tree layer. | Waterfall off an isle (LandmarkEffect curtain from height); chain or root bridges (reuse Bridge spans hung from posts); shrine on the highest isle | None | Mist at the isles' base | **High.** Isles need terrain-independent collision: walkable tops mean a FloorUnder addition like the bridge decks (an isle-top height field per isle, baked). **Fallback:** isles drawn far away only, with no collision beyond 60 m. **Cheapest:** isles out of reach (decorative) at first, walkable later. |
| Moonglade (mine) | Grass with a night-only glow (GlowPulse plus a night flag); pale trees | Moon pool (a pool with strong sky reflection); standing stones (StoneCircle) | — | Night fireflies | Low |
| Tide Flats (mine) | Wet sand and mud (the wet band), rock pools (small pools) | Wrecked boat (driftwood forms) | Sea caves (Rock style, water floor) | Mist, gulls (animal session) | Low |

**Hermit shelters per hostile sub-biome:**
- igloo: a snow-block dome form with a tunnel, plus a blue fire;
- stone lean-to (Ember Forest);
- sandhill hut with a lightning rod (Electric Wastelands);
- obsidian hut (Ashlands and volcano);
- stilt hut (Poison Swamp; reuses Bridge posts);
- mirror-shard tent (Glass Desert; Tent form plus accents);
- mushroom-cap house (Fungal Forest).

Each one is a small landmark with a Home anchor. That's 5 new forms (igloo, lean-to, mound hut, obsidian hut, stilt hut); the other two reuse existing ones.

### 4. Cost summary for kind-possum

- **Flora species:** about 20 new species (most are tint and scale variants of existing forms) × 3 variants × 3 LODs, so about 180 models on top of 351. With 26cd1cd's per-model bounds and LOD skips, draws scale with models visible near the camera, not with the total.
  - The proposal to watch is one shared mesh per layer with multi-command indirect draws (audit item 1).
- **New forms:** about 12 (geode, igloo, lean-to, mound hut, obsidian hut, stilt hut, giant mushroom tweaks, vines, isle, arch, slot-canyon stamp, kiln).
- **Landmark types:** about 18 new, adding to the landmark blob. Bake time grows with placement attempts, roughly +20%.
- **Biome table:** from 14 to about 25 biomes. Per-sample cost unchanged (4 weights); table bytes grow.
- **Weather:** about 3 new presets as assets. The firestorm needs code in the weather files: tell the animal session (Sun) and kind-possum (WeatherField) first. Audit item 4 (WeatherField init loops) should land before it, because init cost scales with presets × biomes.
- **Expensive items and fallbacks:**
  - Floating Isles collision → decorative first;
  - arcs → shader billboards, else static streaks;
  - glass reflections → reuse SSR and fade gloss with distance;
  - jungle canopy → lower density;
  - orbiting debris → vertex-shader motion only.
- **Memory:** blob growth is a few MB. Flora meshes at about 180 models × 3 LODs, roughly +15–25 MB, to be measured.

### 5. Sequencing (proposal)

1. **Realms core**, kept behind a TerrainAuthoring "Use Realms" toggle until approved:
   - Realm assets, two-level placement, adjacency and habitability, hard flora edges;
   - preview map for Zach;
   - existing biomes only, so the island just reorganises.
2. **New sub-biomes in pairs**, cheapest first: Fungal Forest + Ember Forest (reuse), then Mesa + Glass Desert, Crystal Spires + Moonglade, Jungle + Tide Flats, Electric Wastelands, Floating Isles last (decorative first). Each pair is one batch and one sheet.
3. **Caves round 2** after the realms core, so cave styles can be assigned per realm, but before the new sub-biomes' own cave styles. Mines and per-cave variation go in round 2.

**The animal session gets the sub-biome list for fauna.** Kind-possum benches after realms core and after each pair.

### 6. Rules from kind-possum's cost review (2026-10-28), binding
1. No realtime lights for glow, arcs, embers, firestorm, fire whirls, Moonglade, glowing caps: emissive + bloom only; arc flashes go in the sky/ambient term like lightning.
2. Glass, crystal, ice, glass-desert surfaces stay opaque; fake translucency with fresnel/emissive.
3. New surface looks as material properties / per-surface params, not shader keywords (PSO hitches). If a keyword is unavoidable, tell kind-possum first.
4. Floating Isles decorative first; when walkable, gate the isle test in FloorUnder behind its own grid lookup (like Deck); no isle shadows beyond cascade 2.
5. The realm weight for hard flora edges comes from the same 9-cell BiomeCellTableBlob sample, never a second lookup per Place.
6. Audit item 4 (weather init [preset, biome] table) lands before the firestorm.
Watch: flora at realm borders (kind-possum adds a border stop once the preview map exists); jungle Tree-layer instances/ha <= Temperate forest; vertex-motion debris must deform in the shadow pass too; entities file flag at 25 MB, build at 160 MB.

### 7. Living plants (supervisor, for Fungal Forest and Jungle)
The animal session's six living plants (snapping flower, scuttling bush, puffball snapper, wandering toadstool, strangler vine on trunks, sleeping lily pads in pools) need LivingPlant anchors: small ground clearings among matching undergrowth that flora placement leaves clear, plus trunk spots and pool spots. Agree the anchor details with the animal session when that pair starts.
