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

## Measured 2026-11-07 (profiler jobs 24-27, REPORTS-profiler.md): what the numbers changed

**Every stress scene is GPU-bound** at 1080p and 1440p pixels. The main thread sits at 9-12 ms.

**Water is the worst GPU cost by far:**

| Lake view | GPU at 1080p | GPU at 1440p pixels |
|---|---|---|
| From the shore | 25 ms | 46 ms |
| Camera under the water | 38 ms | 64 ms |

For comparison, the big town is 17 ms and the dense forest 13 ms at 1080p. Water is pixel-bound, so **3.4 (water overhaul) is now the top GPU item.**

**CPU, creatures (from cap 180 to 2000; live creatures 264 -> 1330):**

| Cost | Growth | Thread |
|---|---|---|
| Brain | x5.2 | workers |
| Posing | x4.6 | workers; doesn't drop with the brain LOD |
| Creature render | x4.1 | all threads |
| Main-thread brain prep | 0.5-0.8% of the frame | main |

So:
- **2.2 is dropped** (not worth it).
- **2.3 (animate less often by distance) is the next CPU item.**
- 2.4 (cached ground samples) goes with 2.3.

**Results:**
- **3.2 fire instance buffer: merged (PR #201).** LandmarkEffectSystem main thread fell from 4.23 to 1.54 ms in the Ember Forest at night, and from 1.36 to 0.19 ms with 500 fires. It also brings the Ember Forest near/far hand-off fix.
- **3.5b static flora variant: not merged.**
  - It measured within noise: -0.1 ms at the Tors, about 0.5%.
  - Rocks and kit buildings are only 6-21% of the flora triangles in view, and the wind maths is cheap.
  - Branch `refactor/flora-static` kept for reference.
  - 3.5c (per-class LOD and shadow rules) still stands: the big town has 556 shadow casters and the most URP shadow work.
  - 3.5d (a slimmer instance layout) is dropped.
- **Job 28 (2026-11-08, static flora for bigger cities, grouped draws):**
  - Static vs animated: -0.3 ms GPU at the big settlement, -0.2 at the Tors; render thread -0.2 ms at the town. Inside the round-to-round spread, and the split adds about 12-16 SetPass calls.
  - Shader warm-up: not needed. Both variants show the same slow frames 2.5-3.5 s after jumping into a town (streaming, not shader compile).
  - Baked detail: the ceiling (`-nofloradetail`, all procedural detail off) is 0.2-0.3 ms GPU. Not worth building at today's town size.
  - Far shadow cache redraws while walking: +0.4-1.2 ms GPU on 4 frames every 10 m. Scrolling the cache would save under 1 ms on 4% of frames; not worth it yet.
  - Open: one 371 ms frame walking into the big town (1 of 4 runs), cause unknown; needs a capture.
  - Decision for Zach: merge the static variant now (small, grows with city size) or keep it on the branch until the bigger cities exist.

**New priority order:**
1. 3.4 water.
2. 2.3 creature pose rate by distance.
3. 3.1 light manager.
4. 3.5c shadow and LOD rules per class.
5. 3.3 flora density LOD.

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

**3.2 status 11-07:** the instance buffer is done on `refactor/fire-instances` (03783ca9, not merged). Ember Forest looks the same; suite 14/14. Dropped flames are now counted. The timing A/B is BENCH-QUEUE job 27; merge after it. **Ember Forest middle-distance gap:** fixed on `refactor/fire-budgets` (near and far now hand over at the near list's real reach; the cap cut it at 77-87 m of 105 at High). Zach to judge the look in playtest. The eased-lean dictionaries stay managed: they are cleared and swapped, never reallocated, so they make no garbage, and native storage would gain nothing measurable.

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
- **Status 11-07:**
  - **Tent mid-range stripes:** fixed, PR #198. Cause: shadow acne in W-107's cached far shadows (a fixed 0.4 m bias); now a slope-scaled bias.
  - **Tent grid up close:** fixed, PR #199. The hide's lattice seams were removed.
  - **Black rooftops:** not reproduced at `-goto -3329,126.9,1599` (about 15 views: High and Ultra, noon and evening, pitch -20 to 40). Needs a screenshot or the exact view from Zach.

### 3.6 One surface model across renderers (playtest 11-07)
- **The electric golem colossus still doesn't match its rocks.** Colossi (and creatures) draw through their own shader, without the material properties flora rocks get. Give colossi and creatures the same surface inputs as flora (rock grain, triplanar, the biome surface colours) instead of tuning each renderer by hand. Owners: animal (Colossus shader) and world (flora rocks).
- **The mesa arches no longer match the new desert colours.** Objects placed in a biome should take their colours from the biome's land values, as data, so a biome colour change carries over. Owner: world. Find where the placed-object colours are set, and link them to the biome palette.
  **Done 11-07 (PR #200):** FloraSpecies.TakeBiomeRock puts upright rock forms on the boulders' biome-rock colour (G-36). On for SandstoneArch; the hoodoos kept their own banding by choice. Fauna_Fingerprint re-pinned F0EF4BD5 (the new rules field; no placement change).

**Status 11-07:**
- 3.5a and 3.5b are done on `refactor/flora-static` (5febffdb, not merged).
- Measured split on v3: 563 of 1088 variants never move, 30% of LOD0 vertices.
- Looks the same with and without `-nostaticflora`.
- The GPU A/B is BENCH-QUEUE job 26; merge after it.

**Not now:** renaming Flora to WorldObjects in code. It touches namespaces, files, assets, docs, GLOSSARY and every lane's code for no runtime gain, and lanes would collide over it. Use "world objects (flora system)" in docs and cards now. Do the code rename, if ever, together with the assembly-definition pass (4.1).

## 3.7 One detail rule by on-screen size (Zach, 2026-11-08)

**The idea:** every "how much detail" decision uses the same measure: how big the thing looks on screen. That's its
size (max of height and width, or its bounding radius) over its distance, scaled for the field of view and the screen
height. A 30 m colossus at 600 m looks as big as a 1.5 m bush at 30 m, so it should get the same treatment. This
replaces bespoke cases (the colossus and salt tower far-shadow strips, per-layer distances) with one rule.

**What decides detail today (all by distance alone):**

| System | Today | Problem |
|---|---|---|
| Flora mesh LOD | per layer: ground 18 m, shrubs 30/110 m, trees 55/220 m (x quality) | a 2 m rock and a 20 m building in the tree layer switch at the same distance |
| Flora real-time shadows | inside 25 m (`-shadownear`), then the far cache to CacheRange, then nothing | big buildings lose sharp shadows as early as small props |
| Far tree shadows | cheap far caster: crowns drawn as cards with ~1.3 m noise holes | the holes are about one shadow texel far out, so they shimmer ("glitter", playtest 11-08) |
| URP shadow distance | 50 m, 4 cascades | one number for everything |
| Colossi and towers past 50 m | FarShadow: a hand-laid dark strip on the ground, re-laid when the sun turns 1 degree or the body moves 10 m | bespoke; crude from a height; it was laid from the salt colossus's root, not the ball (fixed on `fix/salt-ball-far-shadow` as a stopgap) |
| Creature posing | every frame inside 40 m, every 2nd to 100 m, every 4th beyond | a sea serpent at 100 m is huge on screen, and its stepped motion shows (playtest 11-08) |
| Creature capsule detail | same 40/100 m bands | same |
| Lights | (3.1, not built) | should fade and drop by on-screen size and brightness too |

**The rule:**
- One shared Burst helper: `ScreenSize(size, distance)` = size / distance x (screen height / (2 tan(fov/2))), in pixels.
  Tiers are pixel thresholds, scaled by the quality preset. Equivalent to "switch at distance = size x k", so it's cheap.
- Everything that picks a tier uses it: flora LOD (by each variant's own size, not its layer), flora shadow tier,
  creature pose rate and capsule detail, creature shadows, fire flames, light fade and drop (3.1).
- **Shadows get size-based tiers too:**
  1. Real-time cascades: anything whose shadow is still big on screen, up to the URP distance.
  2. The cached sun-shadow map (today's far cache, 1024): things big enough to matter, out to a few hundred metres,
     redrawn when the sun turns or the camera moves.
  3. A new wide, low-resolution cached map for the very big (colossi, towers, landmarks, large buildings) out to
     1-2 km, redrawn rarely. This replaces the FarShadow strips, so colossi get real shapes from afar, and anything
     big enough qualifies without a bespoke case.
- The leaf-gap holes go: tree crowns past the close range cast through a proper low-detail crown shape or a soft
  density, not per-texel holes, and only species with open crowns get gaps at all.

**Order (each step measurable on its own):**
1. The helper, plus creature pose tiers by on-screen size (smallest change; fixes the sea serpents).
2. Flora LOD by variant size.
3. Shadow tiers by size, with the wide far map replacing FarShadow, and the hole fix.
4. Lights (3.1) built on the same helper.

**Open, needs a repro:** from the basalt tower the salt ball's shadow looked like it added white rather than
darkening. FarShadow multiplies (it can only darken), so this is something else, maybe the real-time shadow's edge
on bright salt. A screenshot from the tower would settle it.

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

## World feel: Zach's ideas, 2026-11-08 (after the refactor, or as prototypes to judge in playtest)

These change how the world looks, so they wait for the freeze to lift unless marked as a prototype. Each is measured
before it ships.

### W1. Water that moves (prototype: branch `water/swells`)
- The sea really rises and falls: four long Gerstner swells from fixed directions (stable when the wind veers, the same
  for every player), as tall as the wind makes them (about 0.2 m calm, 1.1 m in a storm), fading out in the shallows.
  Foam where they break 0.4-4 m off the beach, whitecaps in strong wind. Swimmers ride the swell; the camera's
  underwater check follows it. `-noswells` gives the flat sea to compare.
- Prototype limits: creatures, buoys, driftwood and boats still float at the flat level; no blending yet where water
  meshes of different detail meet (cracks may show); lakes and rivers unchanged.
- Next if Zach likes it: per-biome water feel (height, wavelength, speed, damping: marsh and toxic water slow and
  heavy, mountain lakes crisp), chop from the weather, and standing waves in rapids. Waves don't fix the river stripes
  or the falls edges (mesh and flow-map problems, 3.4).

### W2. Fuller grass fields
- Today: 3D blades (5 triangles each), 28 per m^2 at full density within 20 m, thinned out to 60 m, gusts already
  rolling across fields from a scrolling noise. Only 5 surfaces have grass at all; most biomes are bare.
- The three Godot examples are the same family (opaque 3D blades or alpha-tested cards). Per blade, ours is mid-range:
  cheaper than the "customable" one (4 vertex texture reads, lit), dearer than gdquest's (unshaded, no shadows).
  Alpha-tested cards give the most grass per triangle but cost the most per pixel (overdraw, no early depth reject).
- What makes fields feel full, cheapest first: grass on more surfaces; a "meadow" surface with tall grass; clumps of
  3-5 blades per instance instead of one; the terrain under and beyond the grass painted grass-coloured, with the same
  gust noise darkening it in rolling waves out to the horizon (so the field doesn't end at 60 m).
- **Clumps, not density (Zach, 11-08):** an earlier test of `-grassdensity` showed more evenly scattered blades don't
  read as fuller. The linked shaders draw tufts, and tufts are what sell continuity. Prototype next: each instance a
  clump of several blades fanned from one base (blade count, spread, lean and height varied per clump from its hash),
  fewer instances per m^2, base colour taken from the ground. Then the far-field grass colour and gust shading on the
  terrain. Timed against today's grass at the same spot once built.

### W3. Clouds and clear weather
- The sky ray-marches volumetric clouds per pixel, every frame (up to 160 steps). Fewer clouds save some (clear patches
  still march; scattered cloud is the dearest), but the real saving is drawing the clouds once into a sky texture round
  the player, refreshed a slice at a time, or at reduced resolution with reuse. Job 32 sizes the sky's share.
- The weather leans heavily cloudy: rebalance so truly clear days, thin high cloud and full overcast all happen.

### W4. Flora that clusters by habitat, with clearings
- Today each species already has optional noise patches (PatchSize / PatchCoverage), but about 70% leave them off
  (coverage 1), and the patches ignore the land. So sparse biomes read as an even, uniform scatter.
- Add to placement (seed-derived, in the same Burst jobs), each a per-species weight:
  - **Aspect:** slopes facing the morning or midday sun (saguaros on the sunny hillside), or facing away (ferns, moss).
  - **Shelter:** valleys and hollows out of the wind (big trees), from the height against its local average.
  - **Exposure:** ridges and crests (wind-bent pines, bare rock).
  - **Moisture:** distance to water (exists as NearWater; widen its use).
  - **Clearings:** a biome-wide low-frequency mask that thins everything, with shrubs and flowers along the edges.
  - **Groves:** seeded attractor points that pull one species into dense stands, with stragglers around them.
- Changing placement reshuffles flora, so the fingerprint pins move once (re-pin with the reason). Cost: a few more
  height samples per placement cell; small.

### W5. Moon nights: dark new moons, and the blue moon's insidious pull
- Today: the moon's direct light already scales with its phase (8% at new moon), but the night ambient only drops to
  45% (WeatherSystem: `Lerp(0.45, 1, MoonLit)`), so a new-moon night is never really dark. The blue moon adds a faint
  blue ambient (DayNightCycle.BlueMoonLight x BlueMoonGlow).
- **Dark new moons:** lower the ambient floor to about 10%, so new-moon nights are properly dark (stars, lanterns and
  glowing plants carry them). Free (a constant).
- **Blue-moon nights:** when the blue moon is up and full, the ambient and moonlight turn strongly blue; fog and water
  tint with it. Free.
- **Its influence, in order of cost:**
  1. Blue shooting stars, far more often, all streaking from the blue moon towards the island (the sky shader already
     draws shooting stars procedurally in 6 s slots; this changes their colour, rate and direction). Near free.
  2. Shimmering specks drifting through the sky from the moon towards the island in slow streams, now and then
     (a procedural sparkle field in the sky shader). About 0.1-0.3 ms.
  3. Wavy beams reaching from the moon to the island: faked with a few large soft ribbons of scrolling noise (no
     ray-march), fading where they meet the ground. About 0.2-0.6 ms, by how much of the screen they cover. True
     volumetric beams would cost as much as the clouds (several ms); not worth it.
  4. Other tells, cheap: a low blue mist that gathers in hollows; glowing plants and toxic water pulse in time with the
     moon; a slow ripple of blue light running across the land now and then; creatures restless (fauna lane).

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
