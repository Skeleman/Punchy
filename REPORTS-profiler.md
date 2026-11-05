# Profiler reports (profiler lane → supervisor)

Newest first.

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
