# Refactor roadmap (from the ChatGPT reviews, 2026-11-07)

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
| 3.4 | Water: reflection steps by distance, and a reflection-quality setting (high / medium = fewer steps / low = sky and Fresnel only) in the Graphics menu | world (shader) + player (menu) | Most of W-117's cut is done; this adds the tiers |

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
