# Profiler reports (profiler lane → supervisor)

Newest first.

## 2026-11-05 job 31: GPU split at fishing village 1 (v2, day) and the v3 human village at 22:00 (read only, main 69ca891, uncapped)

**Short answer: it isn't water.** At fishing village 1, flora (7.9-8.8 ms) and camera shadows (7.3-7.8 ms) are the GPU frame, and they overlap: most of the shadow cost is flora casting shadows. Water, post-processing, the opaque/depth copies, sky and structures each cost about 0 ms (within the ±0.5 ms noise). The night town is cheap (gpu 8.2 ms, 100 fps) and has no point lights to cull.

**Method:**
- Profile build of 69ca891, 1 player, uncapped, vsync off. Stops: herewarm, then here 20 s.
- GPU time per frame from FrameTimingManager (the stop line's "gpu").
- The per-pass GPU recorders read nothing on this D3D11 laptop (only FrameTime.GPU of the 237 markers), so each pass was **switched off** and timed against a plain run.
- Switches are new in the bench runner (b/bench-runner): -benchnoshadows, -benchnopost, -benchnocopies, -benchnolights, -benchnoshader <names>. The shader switch turns off the forward and shadow-caster passes of every material using that shader.
- A plain run went first and last at each spot. Plain was fv1 14.5 / 15.0 ms and night 8.2 / 8.2 ms. **Baseline: fv1 14.75 ms, night 8.2 ms; noise about ±0.5 ms.**
- Each "ms" below is the baseline minus that variant. A pass's ms includes its shadow-caster draws, so rows overlap and don't add up.
- Script: lanes/profiler/scripts/q31.sh. Logs: results/q31logs/.

| # | pass | ms fv1 (night) | draws | likely cause | suggested fix | lane |
|---|---|---|---|---|---|---|
| 1 | Flora (5 materials: Flora layers 1-3, cave flora; forward + shadow caster) | **8.35** (3.3) | n/a (see notes) | Dense alpha-tested coastal flora around the village: overdraw in the forward pass, and every flora instance in every shadow cascade. fps 62 -> 99 with flora off. | (a) Flora casts shadows only near the camera (e.g. ShadowCaster only for layer 1 / within ~40 m, or a separate cast distance per layer). (b) Distance density/LOD fade on the far layers. (c) Check that the alpha clip isn't running in the depth/shadow passes of far instances. | world |
| 2 | Camera shadows (all casters, main light) | **7.55** (1.4) | n/a | Mostly the same flora drawn into the cascades: shadows off (7.2 ms) and flora off (6.4 ms) each remove about half the frame, so the shared part is flora shadow casting. At night the shadow cost drops to 1.4 ms. | Fix row 1a first. Then check the URP asset's cascade count and shadow distance at this preset; a shorter distance or 2 cascades on Medium. | world (flora casters); settings (cascades) |
| 3 | Terrain surface | 1.65 (0.8) | n/a | Terrain Surface shader, full screen of ground at the shore. | Look at its texture/noise sample count when next touched. Not urgent. | world |
| 4 | Creatures (-benchnocreatures) | 1.45 (0.4) | n/a | 130 animals at fv1, posed every frame with no segment LOD (job 30, row 3). Part of this is CPU, not GPU: fps 62 -> 67. | Job 30 row 3c (segment LOD for far bodies) cuts both. | animal |
| 5 | Effects (15 materials: soft particles, ambient glow/mist, fireflies, butterflies, flame, plume, haze, precipitation, drips, gas) | 0.75 (0.2) | n/a | Transparent overdraw of ambient life near the shore. | None needed now. | — |
| 6 | Grass blades + pebbles | 0.45 (0.2) | n/a | — | None. | — |
| 7 | Post-processing | 0.05 (0.3) | n/a | — | None. | — |
| 8 | Water (Water, cave water, landmark waterfall) | -0.25 (0.2) | n/a | Not a cost. The opaque-texture refraction is cheap here. | None. The job 30 note guessing "water, wharf" was wrong. | — |
| 9 | Opaque + depth texture copies | -0.15 (0.1) | n/a | — | None. | — |
| 10 | Sky, structures (colossi, far shadow, cave: 12 materials), non-directional lights | about 0 (about 0) | n/a | — | None. | — |

**Night town, for the world lane's distance-lights work:**
- At v3 HumanVillage at 22:00, gpu is 8.2 ms, main thread 7.5 ms, 100 fps. It is GPU-bound by only about 0.7 ms.
- The bench's moving-lights count is 0, and -benchnolights changed nothing (8.2 -> 8.2 ms).
- The game code creates only two kinds of Light component: the lightning flash (WeatherSystem) and cave point lights (CaveSystem). Town lamps and fires are shaders (Flame, Ambient Glow), and switching all effects off saves 0.2 ms.
- **So distance-culling lights would gain nothing measurable at a night town today.** It only matters once real point lights are added. The night frame's GPU budget goes to flora (3.3 ms) and shadows (1.4 ms).

**Notes:**
- **Draw and SetPass counts are missing.** The runner's ProfilerRecorder counters ("Draw Calls Count", "SetPass Calls Count", ...) produced no line in any log.
  - That includes the remake-at-stop-start added for this job, whose code is in the built assembly.
  - The -benchnolights confirmation line didn't print either, so that switch is unconfirmed. The code search above is the evidence that there are no lights to cull.
  - Getting counts needs a Frame Debugger session in the editor, or a RenderDoc capture. I can do that as a follow-up if the world lane wants per-pass draw counts for the flora fix.
- Water was the hypothesis in the job card and in the job 30 note; it's ruled out (rows 8, 9).
- fv1 also has 3.8 ms on the render thread and 10 ms on the main thread. With flora off it reaches 99 fps, so after a flora fix the village is CPU-bound around 100 fps.
- No self-fix: the fixes are in world/render files, not profiler files.
- Cost: 93 calls, peak 285k context, $7.07

## 2026-11-05 job 30: creature system refactor audit (read only, origin/animal-behaviours 115678a)

Zach's question: do behaviours, animation and the other creature logic run as large, slow, serial blocks that could be split into smaller Burst-compiled, batchable jobs?

**Short answer: mostly no, but there are two real problems.**
- The heavy creature work is already Burst and on worker threads:
  - CreatureBrainSystem is an ISystem with a Burst IJobEntity.
  - CreatureRenderSystem's PoseJob and CullJob are Burst IJobParallelFor.
  - The brain already has distance LOD (#59).
  - No managed "god system" runs brain, steering and animation on the main thread. Every main-thread creature system costs under 0.4 ms median.
- Problem 1: **the brain is one big job per entity** (sense + decide + activity + steering + solids in one `Tick`). It runs as only about 4 work items a frame, and the main thread waits for it in the same frame.
- Problem 2: **the pose job has no animation LOD.**
- Those two, plus the sync points around them, are where the milliseconds are.

**Captures:**
- Profile build of 115678a, v2, uncapped, 30 s standing, last 2000 frames.
- Spots:
  - Meadow (`-place meadow`, 185 animals): 98 fps, frame median 10.1 ms.
  - HumanVillage (91 animals): 98 fps, 10.3 ms.
  - Fishing village 1 (130 animals): 62 fps, 15.9 ms.
  - Fishing village 3 (107 animals): 53 fps, 18.6 ms.
- Times below are the per-frame median, with the worst frame in brackets. Worker-job times are summed over all workers.
- Scripts: lanes/profiler/scripts/q30.sh. Data: results/prof30-*.txt.

| # | System | ms (median [worst]) | Problem | Proposed split | Est. gain | Risk |
|---|---|---|---|---|---|---|
| 1 | CreatureBrainSystem: BrainJob (ISystem, Burst IJobEntity) | fv3 7.2 [23.5] worker, of which Brain.Built 5.4 [21.7]; fv1 1.7 [8.9]; meadow 2.2 [10.0]; village 0.5 [1.7] | One job does everything per creature (`Tick`: needs, Decide, activities, Advance, KeepOutOfSolids). It's scheduled per chunk, so it runs as about 4 work items a frame and one chunk of costly creatures serialises. In fv3 the main thread waits 4.2 ms of the 7.2 ms total, so about 1.7 threads' worth of parallelism. The cost is in the solids pass: `SolidAt -> BuildingSink` runs up to 4 `FloraPlacement.Blocked` probes per call, and KeepOutOfSolids calls it for every slide it tries (#132 follow-up). | (a) Take the solids/building test out of Tick into its own **SolidsJob** that runs only over wild land animals near a player, reading a **per-settlement building-footprint grid** baked once at island build (a cell = blocked/sink), not re-running prop placement through `Blocked`. (b) Schedule the per-creature pass as **IJobParallelFor over the snapshot arrays with batch size 8** (as PoseJob does), or IJobChunk with a small chunk capacity, so the work spreads over all workers. (c) Optional: split **Decide/activities** (low rate, already LOD'd) from **Advance/steering** (every frame) into two jobs chained by dependency. | fv3: Built 5.4 -> under 0.5 ms; the brain's wall time about halves from better spread. The main-thread wait (row 2) goes with it. About +15-25% fps at fv3. Meadow/village: 0.5-1 ms of worker time. | Medium. The footprint grid has to give the same answers as `Blocked` (A-44b walls, docks, wharfs); A/B against `-oldwalls`-style switches. (b) is mechanical. |
| 2 | Sync points: CreatureSnapshotSystem (SystemBase), HeldPoseSystem | fv3 snapshot wait 4.2 [20.5] main thread; meadow HeldPose wait 0.56 [2.4] main thread | The brain job is scheduled in Simulation and completed in the same frame by the first reader: HeldPoseSystem (Simulation), or `query.ToComponentDataArray` in CreatureSnapshotSystem at the start of Presentation. The worker time doesn't overlap the main thread's other work. The creature arrays are also copied 4 times a frame: brain, snapshot, CreatureRenderSystem (its own 3 copies) and Mantrap.LateUpdate. | Make the snapshot a **scheduled copy** (`ToComponentDataArrayAsync` / a copy job chained on the brain's handle) taken at the end of Simulation. Readers that can be a frame stale (audio, FolkTalk, Trace, Wake, Breath, Burrow, Mantrap) read **last frame's snapshot (double buffer)**. CreatureRenderSystem uses the snapshot, not its own `ToComponentDataArray` x3. HeldPoseSystem takes the brain's JobHandle as a dependency, not a sync. | fv3: up to about 4 ms of main-thread wait removed (frame-bound by the GPU after that, see note). Meadow: about 0.5 ms. Copies: about 0.1-0.3 ms. | Low-medium. A one-frame-stale state for presentation readers. The render system must still pose the current frame (it already poses one frame late via `FinishPosed`). |
| 3 | CreatureRenderSystem: PoseJob (Burst IJobParallelFor) | fv1 4.0 [22.2]; fv3 2.4 [16.6]; village 2.1 [10.7]; meadow 1.3 [5.8] worker | Full skeleton (`CreatureAnimator.Evaluate`, every segment plus gear) every frame for every creature that isn't culled. The only distance detail is foot-fitting within `FootDetailRange` (40 m). Culling only drops creatures **behind and over 25 m away**; off-screen ones to the side are posed too. Its spikes line up with frames over 33 ms in earlier jobs (27/28: 15-27 ms worst). | (a) **Distance-tiered pose rate**: under 40 m every frame, 40-100 m every 2nd frame, over 100 m every 4th, reusing the last instances moved by the frame's root offset (staggered by entity index so the load is flat). (b) **Frustum cull** with the camera planes, not only "behind". (c) **Segment LOD**: far bodies skip gear, tails and toes (fewer instances, also less draw work). | 40-60% of pose: about 1-2 ms of worker time at villages, and the spikes cut roughly in half. | Low-medium. Visual pop or stutter on far animals if the tiers are too coarse; tune with screenshots. |
| 4 | CreatureAudioSystem (SystemBase, managed loop over the snapshot) | 0.12-0.21 [2.7-3.9] main thread | Per-creature managed scan each frame to pick sound emitters; spikes of 3-4 ms in every capture. | **ISystem + Burst job** that scores creatures and writes the nearest N emitters to a NativeList. Managed code only touches the AudioSources for those N. | About 0.15 ms median; spikes go away. | Low. |
| 5 | TraceSystem (SystemBase, Burst attribute but no job) | 0.03-0.06 [0.6-4.7] main thread | Usually tiny, with periodic 2-5 ms frames (the mean is 6x the median), likely when traces are re-laid. | Move the per-creature trace update into a Burst IJobParallelFor; keep the managed `Write` into the render extras. | Removes 2-5 ms spikes. | Low. |
| 6 | Mantrap (MonoBehaviour) LateUpdate | 0.11-0.12 [0.9-2.0] main thread | `creatureQuery.ToComponentDataArray` every frame (another sync point and copy), then creatures x plants in managed code. | Read the shared snapshot. Do the plant-vs-creature test in a small Burst job, or only for plants within range of a player. | About 0.1 ms; one less sync. | Low. |
| 7 | CreatureWakeSystem, CreatureBreathSystem, CreatureBurrowSystem, FolkTalkSystem, DungeonSpawnSystem, DungeonStandInSystem (all SystemBase, managed) | each 0.02-0.18 [0.2-2.0] main thread | Managed SystemBase with loops over the snapshot; cheap now. Wake has 1.5-3.3 ms spikes in some captures. | Convert to ISystem + Burst when next touched. Wake's per-creature scan goes to a Burst job first. | Under 0.3 ms total. | Low; do last. |
| 8 | InteractionInputSystem: ReachJob (Burst) | 0.16-0.72 [14.5 in fv3] worker | Already Burst; one 14.5 ms frame at fv3, probably scaling with nearby props. | Check what it iterates near many props before splitting anything. | Unknown. | Low. |
| — | FaunaDirector (MonoBehaviour), CritterSystem, AmbientLife, StoneColossi, DunesColossus, GolemArcs | 0.03-0.07 [0.2-2.9] main thread | Spawning, ambient life and colossi. Cheap at runtime. Startup cost is in #102 (FaunaDirector 151 ms in the frame after island ready). | Leave. | — | — |

**Order for the animal lane:**
1. Row 1a, the footprint grid for SolidsJob. It's the biggest cost and the #132 follow-up.
2. Row 2, the async snapshot and double buffer.
3. Row 1b, finer parallel batches.
4. Row 3, pose LOD.
5. Rows 4-7 are cleanups, best done when those files are next touched.
6. SystemBase -> ISystem on its own gains almost nothing here: the managed systems are cheap. The gains come from moving their per-creature loops into Burst jobs (rows 4-6).

**Notes:**
- **Fishing village 1 is GPU-bound now, not creature-bound:** gpu 13.9 ms, main thread 9.3-11.3 ms, DXGI.WaitOnSwapChain 5.3 ms median in the capture. Its brain job is 1.7 ms after #132. The remaining fps gap there is a render cost (water, wharf, props), which is world/render work, not this table.
- BrainJob runs as 4.0 executions a frame in every capture. Whether that's the chunk count is a guess from the profile (creature chunks are large; CreatureState is about 50 fields). The Entities Systems window's chunk count for the creature query would confirm it before doing row 1b.
- Fishing village 3 is bound by both the GPU (15.5 ms) and the main thread (15.3 ms). Rows 1 and 2 remove most of its main-thread excess.
- Cost: 16 calls, peak 250k context, $1.24
