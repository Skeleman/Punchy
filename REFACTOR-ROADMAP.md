# Refactor roadmap (from the ChatGPT reviews, 2026-11-07)

## Picking this up (for a new session or account)
- **Where things are:**
  - Repo Skeleman/Punchy (public).
  - Coordination files live on the `coordination` branch, checked out at D:\Unity\Punchy-coordination.
  - Lane trees on the main PC:

    | Lane | Tree | Branch |
    |---|---|---|
    | Animal | D:\Unity\Punchy | animal-behaviours |
    | World | D:\Unity\Punchy-world | world-features |
    | Player | D:\Unity\Punchy-player | player-client |

  - The profiler lane runs on the laptop, on `b/*` branches, with its queue in BENCH-QUEUE.md.
- **Read first:**
  - COORDINATION.md: rules 1-18, the lanes and file ownership. In particular: rule 15 editor queue, 17 start-quiet, 18 every process ends on its own.
  - SUPERVISOR-HANDOFF.md: latest state.
  - The game's CLAUDE.md and the docs/agents/ file for each area you touch.
  - The three ChatGPT reviews this roadmap comes from (paths below).
- **State on 2026-11-07:**
  - Feature freeze.
  - main at 4fea789d holds every finished card: W-122, A-89, P-20.
  - No lane agents are running. The supervisor is out until about 11-09.
  - Release v0.13.0 is the planned baseline for Phase 1 numbers (see SUPERVISOR-HANDOFF.md for whether it shipped).
  - Uncommitted, unexplained edits sit in the animal tree; SUPERVISOR-HANDOFF.md lists them. Don't commit them blind.
- **How work is handed out:**
  - One card per lane in `batches/<lane letter>-<n>.md`: rows with a "done when" check, pins, and rules. See the last cards (W-122.md, A-89.md, P-20.md) for the format.
  - A lane works only in its own files (the ownership table in COORDINATION.md). Shared files go through the supervisor.
  - Each finished row is committed, pushed and appended to REPORTS-<lane>.md.
  - The supervisor reviews and merges a lane's branch by PR when `git merge-tree --write-tree origin/main origin/<branch>` is clean, then adds a PLAYTEST.md row.
- **Measuring tools that already exist:**
  - `-perflog` on a shot line: frame ms and per-system costs.
  - `-gclog`: garbage a frame, Development builds only.
  - `-camlog`.
  - Shot lists (`-shotlist`, `-shotdir`).
  - BenchmarkRoute and the bench scripts (profiler lane).
  - The profiler lane's SysMs, GcCallers and BigFrame capture tools (lanes/profiler/scripts).
  - Deterministic pins in WorldTests: Landmarks_V3, Fauna_Fingerprint.
- **Suggested first cards:**
  - Phase 0, in one supervisor commit plus one animal row (GLOSSARY).
  - Phase 1.1 and 1.2 for the profiler lane, with a spawn-count flag from the animal lane.
  - Then 2.1 (snapshot double-buffering), the smallest high-value refactor.

Sources: `docs/Chat audit of game code.md`, `docs/Chat audit of light, shadows, water, and fire.md` and
`docs/Chat recommendations for scalability.md`, all in the animal tree (D:\Unity\Punchy\docs). This file turns them into
phases with owners. ChatGPT read the source but never ran the game, so every performance item starts by measuring.

**Feature freeze (Zach, 2026-11-07):** no new feature cards. Lanes finish the row in hand, then only refactor, perf
or hygiene cards from this file. A-89 row 3 (marsh wildlife) is paused; W-122 is done and merged (PR #192).

Checked against main on 11-07:
- Confirmed:
  - CreatureSnapshotSystem allocates three Persistent arrays a frame.
  - CreatureBrainSystem makes 18 TempJob allocations.
  - FireSystem rebuilds and uploads its whole mesh each frame, with managed lean dictionaries.
  - There is one asmdef (the tests).
  - GLOSSARY.md names machines and worktrees.
  - TutorialInfo is tracked.
- Partly done already:
  - W-117 cut the water shader's per-pixel cost (screen-space reflection precompute, low-Fresnel skip, detail reads off past 350 m).
  - W-117 merged far water, cut flora vertices at distance and reviewed flora shadow casters.
  - The forest fire already caps real lights at 6.
  - The P-20 and W-117 garbage rows removed most per-frame GC.

## Phase 0: hygiene (small, do first)
| # | Item | Owner | Notes |
|---|---|---|---|
| 0.1 | `.gitignore`: add `/Assets/Temp/` and `/Assets/_Recovery/` | supervisor (shared file) | Every lane tree has untracked Assets/Temp |
| 0.2 | GLOSSARY.md: drop session, machine and worktree history (public repo) | animal (owns GLOSSARY) | Keep the terms; move lane history to the coordination branch |
| 0.3 | Remove Assets/TutorialInfo and Readme.asset | supervisor | Check nothing references them |
| 0.4 | Review unused packages (Visual Scripting, AI Navigation, Analytics; keep Timeline) | Zach's OK (Packages/) | Housekeeping, not perf |
| 0.5 | Archive stale ownership and queue notes in WORLD.md and MULTIPLAYER.md | supervisor | |

## Phase 1: measure first (prerequisite for 2 and 3)
| # | Item | Owner | Notes |
|---|---|---|---|
| 1.1 | Population sweep in a standalone build: 100 / 500 / 1,000 / 2,000 creatures, all near vs mostly far. Main-thread ms, brain job ms, grid build ms, snapshot copy ms, render CPU/GPU, native memory, GC (`-gclog` exists) | profiler (bench) + animal (a spawn-count flag) | Stop at 2,000: see "impractical" |
| 1.2 | Stress scenes: big settlement (100 kit buildings), dense forest, 500 fires, a lake filling the screen; CPU-bound vs GPU-bound at 1080p and 1440p | profiler | Reuse BenchmarkRoute and SysMs |
| 1.3 | Instrument AI, animation, structure generation and structure activation as separate markers | animal, world | Feeds 2.x and the budget work |

## Phase 2: CPU simulation (animal lane), in order
| # | Item | Notes |
|---|---|---|
| 2.1 | CreatureSnapshotSystem: double-buffered persistent arrays that grow only when the population does | Small, contained, high value |
| 2.2 | CreatureBrainSystem preprocessing: reuse its buffers; build the entity lookup and spatial grid in a Burst job, not on the main thread | Keep the spatial hash; change where and how often it is built |
| 2.3 | Separate AI, movement and animation rates per distance tier (near / mid / far), interpolating between decisions and poses | Thinking less is done; this makes animation less frequent too |
| 2.4 | CreatureAnimator: cache or interpolate ground samples for mid and far creatures | Inside the existing 40 m foot-fitting range |
| 2.5 | CreatureRenderSystem: fewer intermediate arrays in culling and compaction | Only if 1.1 shows it matters |

## Phase 3: GPU rendering budgets
| # | Item | Owner | Notes |
|---|---|---|---|
| 3.1 | Central local-light manager: fires, lamps, torches, interior lamps and forest-fire lights register requests; a budget enables the top N by importance, and the rest stay emissive | player (interiors, lamps) + animal (Ambience) + world (landmark effects) | Highest GPU return. Also fixes "distant lights only switch on near you" (playtest item 6) |
| 3.2 | FireSystem: a static mesh plus a GraphicsBuffer of flame instances (SV_InstanceID); no per-frame vertex and index rebuild; near / mid / far flame budgets with dropped-flame counts in perf logs | animal (Ambience) | Replaces the eased dictionaries too |
| 3.3 | Flora: density LOD (fewer, larger clusters far away), a shadow distance separate from draw distance, wind detail by distance, hysteresis at LOD edges; later, projected-size LOD in the cull compute | world (Flora) | W-117 did the vertex reduction; the rest is new |
| 3.4 | **Water overhaul (Zach plans one in this refactor).** Reflection steps by distance, and a reflection-quality setting (high / medium = fewer steps / low = sky and Fresnel only) in the Graphics menu. Plus the 11-07 playtest's water bugs: the crossing stripe pattern (see the note under this table) and the edge problems on the falls series at `-goto -118,32.1,324`, whose large-scale shape is great | world (shader) + player (menu) | Most of W-117's cut is done; this adds the tiers. Keep the river flow maps and the stepped pools (a good proof of concept) |

- **3.2, Ember Forest flames (playtest 11-07):** distant flames disappear as you approach, then come back up close. That is a gap between the far flame tier and the near tier. Fold it into 3.2's near / mid / far budgets: one hand-off distance with overlap and hysteresis, and lights from 3.1. Zach still wants to iterate on fire lighting.
- **3.4, water stripes (playtest 11-07):** crossing stripes at `-goto -41,54.2,41` and `-goto -1182,12.3,-3020`, probably stepped-pool related. Zach finds it a cool effect, maybe worth making deliberate later as biome detail; for now treat it as a bug.

### 3.5 Flora by behaviour: static objects skip vertex animation (Zach, 11-07; `docs/Chat recommendations for dividing flora.md`)
"Flora" now draws rocks, logs, debris, ruins and whole kit buildings as well as plants. Checked on main:
- Every vertex of every flora model runs the full deformation in Flora.hlsl. There is no static variant.
- The full deformation is the gust sines, the sway bend with its length renormalisation, the flutter sine, and the branches for breath, twitch, vine swing, floating debris and cloth.
- Rocks and buildings carry zero sway, so all of that work changes nothing they show.

| # | Item | Owner | Notes |
|---|---|---|---|
| 3.5a | Classify every flora model by what moves: animated vegetation (wind), static vegetation (dead trees, stumps, roots), static props (rocks, logs, sticks, debris), structures (kit buildings, fences, bridges, ruins), and moving parts (door leaves, part 12; cloth awnings and banners, part 10; vines; floating debris; breathing caps). Log the vertex counts per class at a town and a rock field | world (Flora) | **Classify by parts, not by type:** a kit house with an awning or a door is not fully static. Either split its moving parts into their own small model, or keep it animated |
| 3.5b | A `FLORA_STATIC` shader keyword (or variant) that skips everything after the tilt: no wind, flutter, motions or cloth. Static models draw with that material; culling and indirect draws stay shared | world (Flora.shader, FloraSystem) | Measure the vertex-stage GPU ms in a town, a dense forest and a rock field, before and after. This is where the "massive savings" claim gets tested; expect the biggest win in towns |
| 3.5c | Per-class LOD and shadow rules (with 3.3): vegetation thins with distance; rocks switch mesh LOD; small debris culls early; structures keep their silhouette far away | world | Same cull compute, different parameters per class |
| 3.5d | A slimmer instance layout for static objects (no wind phase or flutter data) | world | Only if 3.5b's numbers show instance bandwidth matters. A second buffer means a second cull path |

Playtest 11-07 bugs that live in the same shader and LOD code (fix them as part of 3.5):
- **Black rooftops:** at `-goto -3329,126.9,1599`, parts of the rooftops are pitch black, and that moves with the camera's pitch. Likely normals or the back-face lighting in Flora.shader.
- **Tents:** the mid-distance LOD has striping artifacts at `-goto 972,12.0,-3845`, and up close the leather shows a grid texture.
- **Contact darkening:** vertex or texture darkening where a structure's vertical face meets the ground (Zach, again). It suits the structures class in 3.5c.

### 3.6 One surface model across renderers (playtest 11-07)
- **The electric golem colossus still doesn't match its rocks.** Colossi (and creatures) draw through their own shader, without the material properties flora rocks get. Give colossi and creatures the same surface inputs as flora (rock grain, triplanar, the biome surface colours) instead of tuning each renderer by hand. Owners: animal (Colossus shader) and world (flora rocks).
- **The mesa arches no longer match the new desert colours.** Objects placed in a biome should take their colours from the biome's land values, as data, so a biome colour change carries over. Owner: world. Find where the placed-object colours are set, and link them to the biome palette.

**Not now:** renaming Flora to WorldObjects in code. It touches namespaces, files, assets, docs, GLOSSARY and every lane's code for no runtime gain, and lanes would collide over it. Use "world objects (flora system)" in docs and cards now. Do the code rename, if ever, together with the assembly-definition pass (4.1).

## Phase 4: modularity (after Phase 2 is under way)
| # | Item | Owner | Notes |
|---|---|---|---|
| 4.1 | Assembly definitions, one leaf module at a time. Measure compile times first; start where nothing depends back (Settings, then Terrain core); DebugControls last | supervisor plans; each lane moves its own | Never big-bang |
| 4.2 | Split by responsibility when a file is next touched: StructureKit (2.4k lines), CaveSystem, FloraPlacement, CreatureBrainSystem partials (perception / needs / activities / navigation / population) | owning lane | No split-only cards |
| 4.3 | Keep StructureKit pure data (KitPart in, no Unity objects); reuse its generation lists | world | Mostly true now; guard it |
| 4.4 | Stage structures: description → mesh and collision prep (budgeted, prioritised by view) → activation → unload | world + player (interiors) | Interior culling (P-20) is a first piece |

## Phase 5: later, only after Phase 1 numbers justify it
- **Persistent distant populations:** creatures and residents as lightweight records far away, materialised as ECS entities near players. A design card with Zach first, because it touches multiplayer ghosts and save state.
- **Shared workload budget:** start as a small registry of per-frame quotas (structures, flora cells, collider activations, NPC decisions) that terrain, flora, structures and fauna all read. Not an adaptive scheduler.
- **Fire as simulation plus presentation:** FireState with tiers and elapsed-time catch-up, and propagation through the spatial grid. Only once fire becomes gameplay.

## Keep as is (ChatGPT agrees)
GPU-driven flora, TerrainSource as the single ground contract, deterministic generation, terrain streaming budgets,
BlobAssets for static world data, procedural creature animation, the river flow-map water, the fire draw-batching
approach (a single draw).

## Impractical, or not worth it now
1. **10,000-25,000 creature tests:** the game caps about 440 live creatures by design (180 wild, 60 settled, 100 cave, 96 critters). Above about 2,000 the tests would mostly measure spawn code and population caps, not the pipeline. Add those sizes after persistent populations exist.
2. **Assembly definitions everywhere:** Entities and Burst source generators run per assembly; DebugControls and the test flags reach into nearly every system (cycles); and parallel lanes would fight over file moves. Worth it only slowly, as in 4.1; the payoff is mainly compile time and enforced boundaries.
3. **Gerstner waves with real vertex displacement:** the water mesh is flat, per terrain chunk, and gameplay reads a flat surface from TerrainSource (swimming, the underwater lens test, foam, wakes). Moving the surface means the CPU must evaluate the same waves to keep TerrainSource the source of truth. Big and cross-cutting for a mostly visual gain; normal-only waves already read well. Skip unless boats or floating objects arrive.
4. **FFT oceans and shallow-water simulation:** ChatGPT rules them out too.
5. **An adaptive, frame-time-driven work scheduler:** high design risk; the quota registry (Phase 5) gets most of the value.
6. **Fire propagation and fuel simulation:** no gameplay needs it yet, and it would be a feature during the freeze.

## Playtest feedback, 2026-11-07 (tabled until after the refactor; on v0.13.0)
Items not folded into the phases above. Each becomes a card row for its lane when features unfreeze. The coordinates
are `-goto` arguments.

| Lane | Item | Where |
|---|---|---|
| world | A cave chamber entrance looks great but you can't walk through the opening | `-goto -1134,62.2,1193` |
| world | The cave entrance mesh and the overworld mesh still don't line up | `-goto 1597,109.9,1060` |
| world | Puddles: none in dunes or very sandy ground (the sand darkens instead), and they should form in recesses so they read as puddles | dunes, sandy biomes |
| world | Desert tomb: column texture on the columns (not brick); a stone-tile floor without grout | desert tombs |
| world | Giant ribcage: walkable bones, and an enemy settlement underneath | ribcage landmark |
| world | Ruin pieces look great: make this kind of landmark reusable in varied forms, some with underground chambers like dungeons | `-goto -2219,120.0,2334` |
| world | The heat haze has a harsh upper cutoff, jarring from the Sonoran desert toward the basalt tower in the flats | Sonoran desert |
| world | Electric Wastelands flat rocks: the collision doesn't follow their pointed edges; approximate the shape better | Electric Wastelands |
| world | Additional forest fire colours: silver, gold, black, deep red | forest fires (palette data; easy once 3.2's instance data has a palette field) |
| world | The inn looks less designed than the modular pieces: flatter textures, less contrast, and its banner didn't rise with the raised overhang. Rework it heavily into the modular kit | inns |
| world | Branch paths leave the main roads only a few metres from their landmarks. Vary their lengths so branches feel like real choices, not roads that run past everything | roads |
| world | Fishing village boats should sit on the water, and they have gaps | fishing villages |
| world | Fishing villages need a large lake, the sea or a river; this one seems to have made its own little lake | `-goto -1238,113.5,315` |
| animal | The giant bone lizard colossus should walk around | bone lizard colossus |
| animal | This colossus looks great: can it swim through the dirt like the snow worms? | `-goto -2271,276.0,1084` |
| animal | Lantern stag: a little faster, and it looks around sometimes, even while walking | lantern stag |
| animal | Snow worms should avoid towns and large obstacles | snow worms |
| animal + player | Many animals show a "pet" prompt but can't be pet: make every animal pettable | everywhere |
| player | Swim underwater as humanoids (and maybe some animals) | water |
| player | Menus: 2D navigation on a controller (up moves to the item above, not the previous one in the list; left and right stop at the UI's edge) | all menus |
