# Profiler reports (profiler lane → supervisor)

Newest first.

## Job 45: R-9 water field and R-10 lights on the laptop, 2026-11-10 (q75.sh)
Profile builds, 1920x1080, `-gfx High`, uncapped, `-weatherseed 4`, Clear, D3D11. Each run is a fresh game, 4 rounds in
alternating order. Bench numbers are mean GPU ms per frame from FrameTiming (median frame ms in brackets). RenderDoc ms are
replay timings, about 2.9x the bench.

### R-9, water/deform fd50ad58: FAIL (+3.7 ms against < +0.3)
**Setup:**
- `-goto 488,-1144` puts the player swimming in the lake (y 9.2).
- New `-benchline` swim mode (Punchy b/bench-stress fa26214d): 10 s settling, then carried east 40 m at 4 m/s.
- `-time 12`, field on vs `-waterfield 0`, rounds on/off, off/on, on/off, off/on.
- The shot shows the swimmer with its V wake and rings. Zach's look note (no leading swell) is the entry below.

| arm | GPU ms by round | mean GPU | median frame | CPU |
|---|---|---|---|---|
| field on | 21.9, 22.3, 21.6, 21.1 | 21.7 | 25.4 | 27.1 |
| `-waterfield 0` | 18.1, 18.0, 18.2, 17.8 | 18.0 | 21.0 | 22.4 |

- **The cost:** on costs +3.7 ms GPU and +4.7 ms CPU, the same in every round.
- **Round 4 (on):** stalled 53.6 s while measuring (the swapchain wait). Its GPU ms still matches the other rounds, so it's
  kept.

**Where it goes (RenderDoc, 3 s into the swim):**
- The whole frame is 57.5 ms off and 63.6 on.
- All of the difference is in DrawTransparentObjects (17.3 to 23.4 RenderDoc ms), in one draw: the lake's water.
  - Off, it's a 23844-index mesh at 10.5 ms.
  - On, it's a **319872-index mesh** at 16.2 ms: 13.4x the triangles, costing +5.7 RenderDoc ms, ~2 ms real.
- So the fine sheet isn't limited to the 32 m square around the player. With Cuts = 8, every quad of the water chunk under
  the player is cut 8x8, across the whole lake.
- **CPU:** the +4.7 ms CPU is most likely building or uploading that mesh each frame. I didn't profile it.
- **Suggested fix:** cut only the quads inside the field's square (the 32 m around the player) and leave the rest of the
  chunk coarse. That should bring both the draw and the CPU close to the off arm.

### R-10, light/forwardplus 0a4b3b50: FAIL against main (+0.9 ms against <= +0.5), but not from the lights
**Setup:**
- `-benchspot 2988.8,-920,0`, facing north, `-benchhour 22`.
- 4 arms on the branch, plus main as today's baseline.
- Rounds in order def, lb0, lb64, tg0, main, then reversed, then repeated.
- **Baseline:** main is ef393625. 06b2a72b and later don't build a player (see the 12:30 note), so I used the last main that
  does.

| arm | GPU ms by round | mean GPU | median frame | CPU |
|---|---|---|---|---|
| default (48 lights) | 16.0, 16.2, 16.0, 16.1 | 16.1 | 16.4 | 16.9 |
| `-lightbudget 0` | 15.9, 16.1, 16.0, 16.0 | 16.0 | 16.4 | 16.8 |
| `-lightbudget 64` | 16.1, 15.9, 16.2, 16.2 | 16.1 | 16.4 | 16.9 |
| `-townglow 0` | 16.2, 16.1, 16.0, 16.2 | 16.1 | 16.5 | 17.0 |
| main ef393625 | 15.2, 15.2, 15.2, 15.3 | 15.2 | 15.4 | 15.8 |

- **Against main:** default is +0.9 ms GPU and +1.1 ms CPU, the same in all 4 rounds.
- **The light arms:** lights 0, 48 and 64 and the town glow are within 0.1 ms of each other. At this spot the R-10 lights
  themselves cost nothing measurable.
- **RenderDoc disagrees with the bench gap:** branch 47.3–47.8 ms against main 48.2. Opaque is 24.1–24.5 on the branch vs
  23.8 on main; transparent is 1.5–1.7 vs 2.5. Main also uses 795 MB of graphics memory against 722.
- **The branch's base is 18df736a,** three merges before ef393625: lod/impostor-trees (#239), fauna/breath-check (#240) and
  world/far-ring (#242). The 0.9 ms may come from that base difference rather than from R-10.
- **For 45b:** I'll add a build of the branch's base 18df736a as a fifth arm, so R-10 is measured against its own base as
  well as main.

### Next
- Job 45b on light/forwardplus 8263a9b5 (row 4c, far lights), as the supervisor queued it. Same spot and arms, plus the same
  spot facing the Ember Forest, plus the 18df736a base.
- Then job 47a.

Files (Punchy-wt results): q75.out, q75-r9-*.csv, q75-r10-*.csv, q75logs/, q75-shots/, rd75/*.rdc, q75-passes.txt.

Cost: this job about $5 (laptop session 0a48ccdb; 3 builds and a Burst-crash retry, 28 games, 7 captures); laptop total $695.

## Note for R-9 (water/deform fd50ad58), 2026-11-10 13:00: Zach's look feedback on the swim wake
I showed Zach a shot from job 45's R-9 run: the player swimming east at 4 m/s in the lake at 488,-1144, noon, `-gfx High`. He
said the wake looks good, except that the water dips around the player at the front edge, where it should swell first.

**What the shot shows:** a trough wraps the front of the swimmer, and the V arms and the rings behind look right.

**Code pointer:** WaterPatchSystem's source job, lines ~206–219. Each swimmer adds two presses at its own position, both
pushing the surface down:
- the dimple (`depth`);
- the shove (`-radius`, `shove`).

Nothing raises the water ahead of it, so the leading edge reads as a dip.

**Possible fix:**
- add a raised hump a little ahead along the motion, about one radius forward, scaled with speed;
- or move the dimple slightly behind the body so the field's own crest leads.

This is for the water lane; I haven't changed anything. Job 45's cost numbers will be on fd50ad58 as built.

**Zach's choice (13:05):** the first option, a raised hump ahead of the swimmer. He says a leading swell is important.

**Zach's request (14:30): finer water at waterfalls, by LOD.** He wants to explore using the R-9 local subdivision at
waterfalls, to show their turbulence more convincingly.
- **Where to cut:** before the fall (the lip and the run-in), on the fall itself, and after it (the plunge pool and the
  outflow).
- **How it switches on:** an LOD distance check on the waterfall, not the player-following 32 m square. Per rule 20 that
  means a DetailPolicy.ScreenSize pixel threshold, so the cut appears as a fall comes close and drops off with distance.

Notes from the profiler:
- **River falls** are the river's own water surface sloping down, so the same copy-and-cut could apply to those quads.
  **Cave falls** are separate meshes (LandmarkEffectSystem) and would need their own cut.
- **The wave field is a height field** (it only moves water up and down), so it won't move a near-vertical sheet. The fall
  needs its own displacement and normals along the flow: the lip, a rippled curtain, churn at the foot.
- **The cut on the fall should run along its length.** The 8x8 cut works in plan view, which leaves a tall drop coarse
  vertically.
- **Fix the job 45 cost first.** On fd50ad58 the patch cuts the whole lake (13x triangles, +3.7 ms GPU on the laptop). Once
  only the needed quads are cut, a fall within LOD range should cost little. I'll measure it when there's a commit.


## Note, 2026-11-10 12:30: main doesn't build a player since #243 (06b2a72b)
- **The failure:** a Profile (Development) player build of 06b2a72b fails to compile. ed0e68ef still has the same code, so
  latest main fails the same way.
  - `Assets/Game/Scripts/Debugging/DebugControls.DungeonWalk.cs` lines 146, 158, 418 and 419 call `TestHooks.BodyPref`,
    `SetBody` and `PutBodyPref`.
  - TestHooks.cs is wrapped in `#if UNITY_EDITOR`, so players get CS0103.
- **Cause:** it came in with A-95 row 1 (daadb490, `-dungeonwalk`).
- **Fix:** those calls need `#if UNITY_EDITOR` around them, or the three helpers need to move out of the editor-only file.
- **Impact on my jobs:** I can't build current main until then. Job 45's R-10 baseline uses ef393625, the main just before
  #243. Job 47 needs a main that builds.

## Job 44: W-129 streaming spikes in a player build, 2026-11-10 (q74.sh)
**Answer: no streaming system repeats over 100 ms outside the editor.** Of the frames over 100 ms that the bench logged:
- About 9 in 10 are the ~500 ms swapchain wait, the one Job 43 hit at the lake.
- Most of the rest are full stalls, or frames right after one.
- Only two frames over 100 ms weren't one of those. Both came from the main thread waiting on Burst jobs. Neither came from a
  streaming system's own main-thread code.

So W-129's 200–590 ms spikes don't come from streaming code in a player build: the ones near 500 ms match this swapchain
wait. The 100–150 ms place below is the only streaming candidate, and it's contention between job systems (W-129 row 3),
not one system.

**Setup:**
- Main ef393625, Profile build, 1920x1080, `-gfx High`, noon, Clear, uncapped, island v3, D3D11, no editor open, each run a
  fresh launch.
- New bench stop `-benchline` (Punchy b/bench-stress 2018d2fd). It settles 10 s at the start, then carries the player along
  the line.
  - Shrub walk: z=1324, x -1236 to 164, at 30 m/s on the ground.
  - Glide: z=-1200, x -3000 to 3000, at 60 m/s, 40 m over the ground or the sea.
- Each run is a pair of launches: a plain one (`-hitchms 50` logs every frame over 50 ms with the player's position), then
  one with the Profiler recording the line.
- Each capture was analysed by `BProfileTools.Analyze -over 50` (5be51e94). For every frame over 50 ms it lists the main
  thread's top samples by self and inclusive time, and the workers' busiest samples.
- **Capture limit:** a capture keeps only its last 2000 frames. So a walk's first 4–6 s and a glide's first ~55 s are only in
  the plain runs' hitch lines.
- **Glides:** 4 glides were needed to get 2 clean ones. 5 of the 8 glide launches stalled while measuring, and a stall skips
  the player ~3 km ahead.
- **Walk1:** the first walk1 pair overlapped a leftover copy of the script and was rerun.

**Per run (mean frame ms, median, and the bench's "hitch" counts):**
| run | mean ms | median | over 50 | over 100 | of those over 100 |
|---|---|---|---|---|---|
| walk 1 | 19.3 | 17.9 | 4 | 2 | 2 ~500 ms waits (x -1229, -1168) |
| walk 1 profiled | 20.3 | 19.0 | 4 | 1 | 1 wait |
| walk 2 | 19.7 | 17.7 | 6 | 3 | 3 waits |
| walk 2 profiled | 21.4 | 19.2 | 9 | 6 | 5 waits; 136 ms on the first frame after settling (x -1236) |
| walk 3 | 19.4 | 17.4 | 6 | 4 | 4 waits |
| walk 3 profiled | 21.3 | 19.3 | 9 | 3 | 3 waits |
| glide 1 | 45.0 | 17.8 | 6 | 2 | a 54.6 s stall at x -2265 and a 4.0 s frame after it |
| glide 1 profiled | 38.5 | 18.7 | 8 | 2 | 1 wait; a 54.2 s stall |
| glide 2 | 32.7 | 18.5 | 11 | 5 | 3 waits; a 54.4 s stall and a 159 ms frame right after it |
| glide 2 profiled | 20.7 | 18.8 | 37 | 9 | 9 waits (clean run) |
| glide 3 | 32.8 | 18.5 | 19 | 6 | 4 waits; a 56.0 s stall and a 147 ms frame right after it |
| glide 3 profiled | 20.6 | 18.7 | 24 | 10 | 10 waits (clean run) |
| glide 4 | 27.5 | 18.1 | 19 | 8 | 4 waits; a 27.6 s stall and 107/142 ms frames right after it; 143 ms at x -1515 |
| glide 4 profiled | 40.7 | 18.7 | 15 | 7 | 3 waits; a 57.7 s stall and a 104 ms frame after it; 134 ms at x -1524; 104 ms at x -4 |

**The ~500 ms waits (449–551 ms, 51 of them):**
- In the profiled runs every one is the main thread in `DXGI.WaitOnSwapChain` (under `WaitForLastPresentation`) for
  490–530 ms. Main-thread work in the same frame is under 20 ms. This is the same wait as the long stalls (job 41).
- They gather where streaming is heavy: the walk's first 70 m (x -1236 to -1168) and the glide around x -1800 to -1440.
- **I think the GPU or driver is busy with something the CPU doesn't see,** such as uploads or pipeline creation, and the
  present blocks until a ~0.5 s timeout. That's a guess.
- At 1–10 per run, they're the biggest source of frames over 100 ms in the player build.

**The non-wait frames over 100 ms, with their top samples:**
- **x -1520, z -1200, the glide** (first visit; 143 ms plain and 134 ms profiled in glide 4, 69–84 ms in glide 1 profiled):
  - Main thread: waits 114 ms in `WaitForJobGroupID` on `FloraSystem:PlaceJob`, from `FloraSystem.LateUpdate`.
  - Workers in the same frame: FloraSystem:PlaceJob 194 ms, CaveSystem:MeshJob 134, BuildChunkMeshJob 114,
    SurfaceGlow:FarScanJob 105, SurfaceGlow:ScanJob 80.
  - The flora placement complete lands while caves, terrain meshes and glow scans fill the workers. This is the contention
    W-129 row 3 asks about.
- **x -4, the glide (glide 4 profiled), 104 ms:**
  - Main thread: waits 79 ms in `WaitForJobGroupID`.
  - Workers: FloraSystem:PlaceJob 303 ms, BuildChunkMeshJob 69.
- **The walk start, 136 ms (walk 2 profiled):** the first frame after settling, before the capture began, so it has no
  samples.
- **Frames right after a stall (104–159 ms):** catch-up after a 27–58 s frame. The line skips ahead, so these are not
  streaming.

**Frames of 50–100 ms (from the captures):**
- **Main thread waiting on jobs.** About half are `WaitForJobGroupID`, usually on `FloraSystem:PlaceJob`. Its workers total
  140–490 ms in those frames, with `GroundDetailSystem:SampleGroundJob` and `CaveSystem:ColumnJob` the next most often.
- **`TerrainStreamingSystem`'s own main-thread time.** It runs 30–63 ms (self) on single frames of the glide.
  - Repeated at 7.6–7.7 s, 29 s and 38–40 s into the capture in both clean glides.
  - The top was 63 ms near the glide's end, with `EngineJob` 91 ms and `WideShadow:HeightJob` 38 ms on the workers.
- **Others, once each:**
  - `CaveSystem.Update` running Mono.JIT (10–16 ms) at 21 s in both clean glides. This is first-use JIT.
  - `Instantiate.Copy` 15 ms with `ColossusWalker.Update` 9–10 ms at 33 s in both.
- **`FloraSystem.LateUpdate` grows** from ~3 ms to 10–20 ms self on the busiest glide frames, around 38–40 s into the
  capture.
- None of these reach 100 ms by themselves.

**For W-129:**
- Closed as "no streaming system over 100 ms outside the editor".
- Two items to keep:
  1. **The x -1520 glide contention.** Flora placement's Complete lands behind cave and chunk-mesh jobs, 70–143 ms on a first
     visit.
  2. **TerrainStreamingSystem's 30–63 ms main-thread frames on the glide.**
- The ~500 ms swapchain waits belong with the swapchain stall work (b/fix-swapchain-stall, job 42), not with streaming.

Files (Punchy-wt results):
- q74.out and q74b.out
- q74logs/
- q74-*-p.txt: the OVER lines

Cost: this job about $4 (laptop session 0a48ccdb; 28 launches and 7 analyses); laptop total $690.

## Job 43: water, SSAO, glow slots and shadow distance on the laptop, 2026-11-10 (q73.sh)
Main 2644b295, Profile build, 1920x1080, `-weatherseed 4`, noon, Clear, uncapped, D3D11, `-gfx High` and `-gfx Medium`.
Each run is a fresh game, round 1 in list order and round 2 reversed, about 300 measured frames. 56 bench games ran from
06:00 to 08:23, then 14 RenderDoc captures at `-gfx High`. Bench numbers are the mean GPU ms per frame from FrameTiming
(median frame ms in brackets). RenderDoc ms are replay timings, about 2.9x the bench GPU ms on this laptop (a 46 ms RenderDoc
frame against 15.7 ms in the bench). So I scale them to real ms by each capture's own frame ratio, marked "~".

**Skipped, because main has no flag for them (no code added):**
- SSAO on vs off. Its pass cost from the captures is below.
- Glow slots at 22:00.
- A 10% water view. At job 39's lake (`-benchspot -1624,-488` facing 270, player at y 34), pitch 0 shows about 45% water and
  pitch 30 about 80%. Every upward pitch puts the camera under the lake. The Beach finder faces a sand bank here.
- 120 m shadows. `-gfxset shadows=` is 50 m x Shadows, clamped at 2, so 2.0 = 100 m is the most. Also, 1.0 casts 3 cascades
  while 1.4 (High's own), 1.6 and 2.0 cast 4, so the 50 m arm also drops a cascade.

**Over 1 ms:**
- **Water is the biggest item.** The water surface is one 24576-index draw in DrawTransparentObjects. At gfx High that pass is
  ~7 ms of GPU at the 45% view and ~9 ms at the 80% view.
- **Water quality High over Low** at gfx High, 45% view: +3.9 ms (median frame +2.6 ms). Medium over Low: +2.5 ms. At gfx
  Medium it's +1.6 ms and +0.8 ms. At the 80% view the gap is smaller: High over Medium is +0.4 ms at both gfx levels.
- **SSAO is under 1 ms** (~0.6 ms).
- **Shadow distance 50 to 100 m** costs +0.3 to +0.7 ms of whole-frame GPU. The shadow pass alone grows by ~0.7 ms. Under 1 ms.

**New: ~500 ms hitches at the lake.**
- Frames of 496–541 ms come back every few seconds with water quality High or Medium at gfx High: 2–7 per game in 7 of 8 games.
- None in the two clean Low games at the 45% view. At gfx Medium: 1–2 per game in 4 of 12 games. Shadow runs: 3 of 32 games.
- The log shows only the hitch lines. These hitches inflate "slow", avg and 1% low, so I compare GPU ms and median frame ms.
- I haven't found the cause. It may be related to the swapchain stall (0.5 s is a typical present-wait timeout), but that's
  a guess.

**Water, bench GPU ms (median frame ms):**
| gfx, view | quality High | Medium | Low |
|---|---|---|---|
| High, 45% (pitch 0) | 22.1 (22.9) | 20.7 (21.6) | 18.2 (20.3) |
| High, 80% (pitch 30) | 22.2 (23.2) | 19.8 (22.2), r1 only | no clean round |
| Medium, 45% | 17.2 (18.7) | 16.4 (18.2) | 15.6 (17.2) |
| Medium, 80% | 18.4 (18.3) | 18.0 (18.0) | 17.7 (16.9), r2 only |

Dropped runs had a stall inside the measured window: High-80%-Low (both rounds), High-80%-Medium r2 and Medium-80%-Low r1
(timed out at 900 s).

**Water passes from RenderDoc at gfx High (RenderDoc ms, frame / DrawTransparentObjects / the water draw):**
| view | High | Medium | Low |
|---|---|---|---|
| 45% | 48.7 / 16.0 / 8.9 | 57.1 / 15.0 / 7.9 | 55.6 / 12.9 / 6.4 |
| 80% | 54.7 / 21.6 / 12.2 | 54.4 / 21.1 / 11.9 | 53.2 / 19.9 / 11.1 |

- The rest of the transparent pass is 59 small instanced draws, plus at the 80% view two more 24576-index water tiles of
  ~0.75 ms each.
- The 45%-High capture has a lighter opaque pass (19.4 against 24 ms) and prepass (5.5 against 11 ms), so it was taken before
  the terrain fully streamed. Use its transparent numbers only.
- With the camera low over the lake, Low-res effect is 2.9 ms at the 45% view and 0.06 at the 80% view. Skybox goes 0.63 to
  0.01 for the same reason.

**SSAO (RenderDoc ms, gfx High):**
- Forest 1.73–1.77, town 1.87–1.89, about 3.7% of the frame. That is ~0.6 ms real. Its main draw is ~1.3 RenderDoc ms.
- The lake views are 1.16 (45%) and 1.49 (80%).

**Shadow distance (bench GPU ms, median frame ms in brackets; RenderDoc "Draw Main Light Shadowmap" ms after the slash):**
| spot | 50 m (1.0, 3 cascades) | preset | 80 m (1.6) | 100 m (2.0) |
|---|---|---|---|---|
| High, forest | 15.3 (17.9) / 2.40 | 70 m: 15.7 (18.4) / 3.81 | 16.0 (18.5) / 4.09 | 16.0 (18.5) / 4.41 |
| High, town | 16.6 (19.4) / 1.29 | 70 m: 17.0 (19.8) / 2.41 | 17.0 (19.9) / 2.54 | 16.9 (19.9) / 3.38 |
| Medium, forest | 12.7 (14.7) | 50 m: 12.5 (14.8) | 13.0 (15.1) | 13.0 (15.1) |
| Medium, town | 14.1 (16.5) | 50 m: 14.2 (16.4) | 14.1 (16.8) | 14.5 (16.9) |

- **Noise floor ~0.2 ms.** Medium's preset is the same setting as 1.0, and the two arms differ by up to 0.2 ms.
- **The shadow pass alone:** 50 to 100 m costs ~0.7 ms real at the forest and ~0.7 ms at the town (RenderDoc 2.4 to 4.4 and
  1.3 to 3.4).
- **Most of that is the 4th cascade.** 50 to 70 m (the 3-to-4 cascade step) accounts for most of it. 80 to 100 m on 4
  cascades adds ~0.1 ms at the forest and 0 at the town.
- **Opaque, prepass and SSAO don't move with shadow distance.** Forest opaque is 22.5 RenderDoc ms at every setting.
- Forest whole frame is 14.5 ms of prepass and 22.5 of opaque at RenderDoc scale. That's the job 39 picture.

**Stalls (the swapchain wait from job 41) are frequent on 2644b295:**
- 16 of 56 bench games had a frame of 50 s or more: 14 of 24 lake games and 2 of 32 forest/town games.
- Most ended at 50–60 s, when the sampler's cdb attach released them. Others ran to 344, 656 and 810 s, and one hit the 900 s
  timeout.
- 4 were in the measured window: the three lake rounds dropped above, and High town 1.0 r1. That round is kept because its GPU ms (16.5) matches r2 (16.7).
- One RenderDoc game (80%-Medium) stalled after its capture.

Files (Punchy-wt results): q73.out, q73-water-*.csv, q73-shadow-*.csv, q73logs/, q73-shots/, rd73/*.rdc, q73-passes.txt.

Cost: this job about $5 (laptop session 0a48ccdb; 56 games and 14 captures); laptop total $686.

## Job 42: Summit stall swapchain fix A/B (b/fix-swapchain-stall c9672fcc), 2026-11-10 (q72.sh)
The branch is main 33fe6457 plus two start-up flags, both set from script (FrameRateCap.cs) with no ProjectSettings change:
`-vsync N` (QualitySettings.vSyncCount) and `-maxqueued N` (QualitySettings.maxQueuedFrames). Each game logs the values it
ended up with; the default is vSyncCount 0, maxQueuedFrames 2. Profile build, job 41's setup (Summit facing out, pitched up 10,
1920x1080, `-gfx High`, `-weatherseed 4`, noon, uncapped, `-lowres half`), D3D11 (every log says Direct3D 11.0). 24 rounds, each
one fresh game per arm in shuffled order, so 120 games from 23:51 to 03:43. A stall counted as a frame of 30 s or more, or a log
quiet for 120 s (then cdb once per arm, then a kill by PID). freezestall's own cdb attach was off for the job. The runcsv `i`
bug is fixed first (Punchy b/bench-stress 1e1374b0: wait_s and cool keep their loop counter local).

**Stalls: none in any arm, the control included.** 0 in 24 games for each of A (no flags), B `-maxqueued 1`, C `-maxqueued 3`,
D `-vsync 1` and E `-force-d3d11-no-singlethreaded`. No frame over 30 s, the watchdog never fired, and freezestall saw no quiet
log. Job 41's D3D11 rate (3 in 12) would give 0 in 24 about 0.1% of the time, so **the stall didn't reproduce, and this A/B
can't tell whether any arm fixes it.** Per the card I'm stopping here, with no default changed in the branch.

What differs from job 41:
- main 0e964452 became 33fe6457 (#222-#226: town lights, wide shadows, the shots tool, world fixes, breath size). None of these
  touches presentation, timing or flora tasks.
- No automatic cdb attach, though that can only end a stall, not prevent one.
- The time of day: job 41 ran 15:40-17:00, and jobs 38-39 ran in the daytime too; this job ran overnight with the laptop untouched.

If the stall comes back, the flags are on the branch for a rerun.

**fps at the Summit (24 games per arm, none stalled; the spread across games is in brackets):**

| arm | avg fps | 1% low | CPU ms | GPU ms | fps vs A |
|---|---|---|---|---|---|
| A control (queued 2) | 70.4 (68.5-71.7) | 44.8 | 14.22 | 9.82 | |
| B `-maxqueued 1` | 31.5 (30.4-34.9) | 15.1 | 31.79 | 13.00 | **-55%** |
| C `-maxqueued 3` | 80.2 (78.3-81.3) | 53.1 | 12.47 | 12.23 | **+14%** |
| D `-vsync 1` | 57.2 (56.1-58.0) | 25.2 | 17.48 | 10.98 | -19% (vsync; the cap sits under 60) |
| E `-force-d3d11-no-singlethreaded` | 70.3 (68.1-71.4) | 45.3 | 14.22 | 9.79 | 0% |

Even with no stall to fix, two of these are worth knowing:
- **One queued frame halves the frame rate here.** The CPU waits for the GPU every frame.
- **Three queued frames gain 14%,** with no overlap between the arms' ranges. The GPU ms rises because each frame's timer now
  includes more queueing, not more work. The cost is one more frame of input latency.

-force-d3d11-no-singlethreaded changes nothing.
Cost: this job about $6 (laptop session 0a48ccdb; 120 games overnight); laptop total $681.

**Addendum 05:10, the stall is still there on main 2644b295.** In job 43's first game (a fresh Profile build, the Beach place,
04:46), the game went quiet twice, both times with CPU and GPU idle:
- 60 s during loading, ended by freezestall's cdb attach at 04:47:45;
- 640 s just after the jump (`a 638.1 s frame at 0.2 s into the stop`), with no attach because cdb attaches once per game. It
  ended on its own at 04:58:37.

So job 42's 0 in 120 means the stall got rarer, or depends on something that run didn't hit, not that it's gone. Both job 41's
first game and this one came straight after a fresh build. With the stall this rare, the A/B needs a way to trigger it before
it can tell the arms apart.

## Job 41: what the Summit stall waits on (main 0e964452), 2026-11-09 (q71.sh)
Profile build, job 38's Summit setup (facing out, pitched up 10), 1920x1080, `-gfx High`, `-weatherseed 4`, noon, uncapped,
`-lowres half`, plain weather. 24 fresh games alternating the default API with `-force-d3d12`, round 2 starting on D3D12. The
default on this build and laptop is **D3D11** (the logs say `Direct3D 11.0 [level 11.1]`), not D3D12, and the GPU is a GTX 1060 Max-Q,
not Intel; the `-force-d3d12` games say `Direct3D 12 [level 12.1]`. With Zach's OK I installed cdb (WinDbg) and procdump.
freezestall.ps1 now attaches cdb non-invasively on a game's second stall snapshot (about 45-50 s into a stall), once per game,
and saves every thread's stack with Microsoft and Unity symbols.

**Stalls (frames of 30 s or more):** D3D11 3 in 12 games (56.5, 56.0, 52.5 s); D3D12 6 in 12 games, in 4 of them (50.1 then
500.6 s in one game, 54.2 then 1080.7 s in another, 55.6, 51.9). Shorter ones: D3D11 22.0, 24.3, 5.1 s; D3D12 12.2 s. Both APIs
stall. D3D12 is also slower here when it isn't stalled: 61-64 fps against 72-76 fps, with CPU at 16 ms against 13.5 ms.

**Where the threads sit (all 13 stall snapshots, both APIs):** the main thread ("Unity Main Thread") is in Wait/UserRequest and the
window is not responding. The slow-polling thread (the Audio Mixer Thread, priority 13, start address UnityPlayer.dll+0x2e4b1b0)
is also in Wait/UserRequest and is just the mixer ticking. The GPU is at 0% with idle clocks, and the game uses about 0.03 s of
CPU per second. There's no disk, Defender or CPU load.

**cdb stacks (7 stalls, one per game):** two caught the main thread still waiting, on both APIs, in the same place:
```
ntdll!NtWaitForMultipleObjects / KERNELBASE!WaitForMultipleObjectsEx
UnityPlayer!SwapChainWaitingContext::WaitOnSwapChain
UnityPlayer!GfxDeviceD3D12::WaitForLastPresentationAndGetTimestamp   (D3D11 game: GfxDeviceD3D11::...)
UnityPlayer!...TimeUpdateWaitForLastPresentationAndUpdateTimeRegistrator::Forward
UnityPlayer!ExecutePlayerLoop / ExecuteTimeUpdate / MainMessageLoop
```
That is the start of the player loop, waiting for the last frame's presentation through the swapchain's waitable object. The
UnityGfxDeviceWorker is idle on a futex, and the NVIDIA driver threads (nvwgf2umx) sit in their own waits. No managed code is on
the stack, and there is no Monitor, Task or `task.Result` wait anywhere on the main thread, so **it isn't TakePregrown.**
Classification: **the presentation/swapchain wait, on the driver side, under both D3D11 and D3D12.**

**Attaching cdb ends the stall.** In all 7 games the game came back to life within 8 s of cdb attaching: CPU climbs, the GPU goes to
55-99%, and the log grows again. Three of the stacks already show the main thread back in LateUpdate or culling, and the two
shutdown stacks are games that resumed and quit within seconds. That's why every stall in this job lasted about 50-56 s, which is
when the attach fires. Without an attach they run long:
- the second stalls in the two D3D12 games that had already been attached once lasted 500.6 and 1080.7 s, and freezestall's
  read-only snapshots, which ran every 30 s through both, didn't end them;
- jobs 38-39, before cdb, had stalls of 41.8 to 1622 s, with a median of about 500.

A non-invasive attach suspends and resumes every thread and injects a break-in thread. So the stall looks like a missed wakeup:
the swapchain's wait object never signals (the GPU has nothing queued) until something disturbs the process. The stalls still
fall just after the Summit jump, mostly while settling at 0.2-7.7 s into the stop, and twice while measuring.

For the frame-latency/swapchain card: this points at the waitable-swapchain present wait itself, not at the GPU work. Things to
try: a timeout or a fallback on that wait, `-force-d3d11-no-singlethreaded`, a different `QualitySettings.maxQueuedFrames`, or
VSync on. I can bench any of them here; with cdb now in place, a stall that ends right at an attach is the tell.

Script bug: runcsv reuses the loop's `i`, so the per-game labels in q71.out (g1-g13) are wrong, and of the 24 game logs only 23 were
kept (11 D3D11, 12 D3D12; same-named ones overwrote). The counts above come from the lines printed right after each game, so they
cover all 24. Stacks are in results/freezestacks-<pid>.txt and snapshots in results/freezestall.txt (laptop only).
Cost: this job about $2 (laptop session 0a48ccdb, including the cdb stack tooling); laptop total $675.

## Job 40: R-4 sky cost on the laptop (sky/look 5880293c vs main 5558f89a), 2026-11-09 (q70.sh)
Profile builds of both, job 34's Summit setup (place Summit, pitched up 10), 1920x1080, `-gfx High`, `-weatherseed 4`, noon,
uncapped, a fresh game per run. Each list ran in order, then reversed. GTX 1060 Max-Q on D3D11.

**Whole-frame GPU ms (mean of 2 rounds; rounds within 0.3 ms):**

| | main | R-4 | R-4 `-skyoff 896` | R-4 `-benchnosky` | R-4 minus main |
|---|---|---|---|---|---|
| half, plain | 10.75 | 10.65 | 10.8 | 6.2 | **-0.1** |
| quarter, plain | 9.9 | 9.9 | 9.9 | 6.4 | **0.0** |
| half, Overcast | 10.75 | 10.55 | | | **-0.2** |
| half, Rain | 11.9 | 11.85 | | | **-0.05** |

Pass: R-4 is within +1 ms of main everywhere. It is no cheaper either: the sky costs about 4.5 ms over no sky at half and
3.5 ms at quarter on both builds. CPU ms match within 0.3 ms. Rain costs 1.1 ms more than plain or Overcast on both builds.

**RenderDoc (R-4, half), against job 38's half capture (main acd0cad5):**

| pass | job 38 main | job 40 R-4 |
|---|---|---|
| DrawSkybox | 3.68 | **3.26** |
| Low-res effect | 4.38 | 4.82 |
| sum | 8.06 | 8.08 |

DrawSkybox dropped 0.42 ms, but the low-res effect grew by 0.44 ms, so the total is unchanged. DrawSkybox is still a single
full-resolution draw (5040 vertices) at 3.26 ms, and it is still the largest sky pass that doesn't scale with `-lowres`. It looks
like R-4 moved the work into the low-res buffer at half without removing any. (The two captures come from different main commits;
prepass and opaque were 8.9/13.6 ms here against 9.6/14.5 ms in job 38's half capture, and the whole frame was 36.5 against 38.0 ms.)

No stalls in this job: freezestall.txt has no snapshots from q70's 25 games.
Cost: this job about $1 (laptop session 0a48ccdb); laptop total $674.

## Job 39: depth priming on the laptop (main 5558f89a, R-3 #220), 2026-11-09 (q69.sh)
Profile build, 1920x1080, `-weatherseed 4`, noon (`-benchhour 12`), Clear forced over each stop, uncapped, a fresh
game per run, ABBA `-depthpriming forced` / `off` (the log confirms Forced / Disabled each game). One `-benchspot` fits
in a game, so each run is two games: big town (the bigtown stop, which lands on 2972,-885), dense forest (spot
1740,3355, facing 180) and Summit (place, -2446,1154); then the lake (spot -1624,-488, facing 0; the card gave no facing).
The laptop's GPU is a GTX 1060 Max-Q on D3D11, not an iGPU.

**Whole-frame GPU ms (mean of 2 rounds; rounds within 0.8 ms except where noted):**

| spot | High forced | High off | off costs | Low forced | Low off | off costs |
|---|---|---|---|---|---|---|
| big town | 16.4 | 23.8 | **+7.4** | 11.65 | 14.0 | **+2.35** |
| dense forest | 15.8 | 28.15 | **+12.35** | 9.05 | 17.45 | **+8.4** |
| Summit | 11.05 | 15.3 (1 round) | **+4.25** | 4.45 | 9.75 | **+5.3** |
| lake | 22.4 (20.5 / 24.3) | 39.35 (44.5 / 34.2) | **+17** | 16.6 | 24.2 | **+7.6** |

- **Forced wins at every spot and both presets, by far more than on the desktop.** `off` is never close: 2.35 ms
  worse at best (Low, big town), 8.4 ms worse in the forest at Low.
- **So keep Forced at Low too; don't turn priming off on the Low renderer.**
- `off` also brings hitches at High: 81-115 slow frames per stop in the town and forest (forced: 1-3), 242-572 at
  the lake (forced: 14-139).
- The lake at High is noisy in both modes (rounds 4-10 ms apart), but every round of `off` is worse than every round of
  `forced`.
- Missing: High `off` round 2 timed out (1800 s) before its Summit stop, after a 150 s stall in the town (see below),
  so High `off` at the Summit is one round.

**Prepass and opaque pass ms:** all 16 RenderDoc captures are saved (`results/rd69`). The pass replay didn't run: a
path bug in q69.sh saved the captures under a broken name (fixed, d4dfcf55). It runs after job 41 (the replay mustn't
share the GPU with a bench game) and goes in as an addendum here.

**Stalls:** 5 of the 16 games stalled after a jump: 985, 837, 496, 317 and 150 s (98 + 42 + 9 in one game). Four
stalls were settling; one (837 s, lake, High forced, round 1) was 28 s into the measured part. That round's GPU mean
(20.5) is the lower of the two, so the stall didn't inflate it. In every snapshot the game's main thread waits
(UserRequest) and the window doesn't respond. The thread using a little CPU is priority 13, which matches Unity's
audio mixer thread.

**Tools (Zach OK'd them, 11-09):**
- cdb (from WinDbg; it won't run from WindowsApps, so it's copied to `~/Tools/cdb`) and ProcDump (`~/Tools/procdump`).
- A stall of 45 s or more now gets a non-invasive cdb attach: every thread's stack, with Microsoft and Unity symbols,
  in `results/freezestacks-<pid>.txt` (`freezestacks.ps1`).
- Snapshots now show thread names and start modules.
- `pgrep` doesn't exist in this Git Bash, so each q-script started a second sampler. Fixed with a `ps` check, and the
  duplicate is stopped.

Cost: this job about $5 (laptop session 0a48ccdb, including the stall tooling); laptop total $673.

**Addendum, 2026-11-09, RenderDoc pass ms** (the 16 q69 captures, replayed with rdpasses.py; RenderDoc replay times run about 3x the
bench, so read them as ratios). The depth prepass costs the same with priming off as with it forced. Only the opaque pass changes:

| capture | prepass forced / off | opaque forced / off | opaque saved |
|---|---|---|---|
| High big town | 12.6 / 11.9 | 29.6 / 55.3 | 25.7 |
| High forest | 14.1 / 13.4 | 21.1 / 52.7 | 31.6 |
| High Summit | 9.9 / 10.0 | 17.9 / 35.8 | 17.9 |
| High lake | 10.5 / 10.0 | 27.0 / 66.4 | 39.5 |
| Low big town | 4.15 / 4.17 | 19.6 / 34.5 | 15.0 |
| Low forest | 5.34 / 5.29 | 11.9 / 35.6 | 23.6 |
| Low Summit | 2.71 / 2.76 | 8.0 / 13.8 | 5.8 |
| Low lake | 4.02 / 3.87 | 18.7 / 37.6 | 18.9 |

The DepthNormal prepass runs either way (SSAO needs it), so forced priming just lets the opaque pass reuse that depth (Z equal)
and skip overdraw. It costs nothing extra on this GPU, which is why forced wins everywhere. DrawSkybox is 0.2-1.4 ms in these
views.

## Job 38: why quarter-resolution sky isn't cheaper than half (main acd0cad5), 2026-11-09 (q68.sh)
Job 34's setup: Profile build, the Summit facing out, pitched up 10, 1920x1080, `-gfx High`, `-weatherseed 4`,
uncapped, noon, plain weather, a fresh game per run. Two rounds (round 2 in reverse order), within 0.6 ms.

**(1) GPU ms at the Summit** (mean of 2 rounds; main thread in brackets):

| | default | `-skyoff 1` (no march) | `2` (no far clouds) | `4` (no sky colour) | `8` (no weather volumes) | `-benchnosky` |
|---|---|---|---|---|---|---|
| `-lowres half` | 10.9 (14.1) | 9.7 (11.9) | 10.7 | 10.9 | 10.9 | 7.0 (11.1) |
| `-lowres quarter` | 10.3 (12.4) | 9.6 (11.8) | 10.4 | 10.4 | 10.4 | 7.0 (10.9) |

- The whole sky costs 3.9 ms at half and 3.3 ms at quarter (default minus `-benchnosky`).
- The march is the only part that shrinks with size: 1.2 ms at half, 0.7 ms at quarter (default minus `-skyoff 1`).
- **Without the march, 2.6 ms of sky is left at both sizes.** Bits 2, 4 and 8 each change it by 0.2 ms or less.

**(2) RenderDoc, one frame each** (RenderDoc's timings read about 3.5x the bench's on this iGPU, so compare them with
each other, not with the table):

| pass | half | quarter |
|---|---|---|
| Low-res effect (the march, 1 full-screen triangle) | 4.38 | 1.56 |
| **DrawSkybox (Camera.RenderSkybox, 1 draw, 5040 indices, full resolution)** | **3.68** | **3.70** |
| Low-res depth | 0.05 | 0.06 |
| Low-res resolve (upsample) | 0.11 | 0.03 |
| DrawOpaqueObjects / DrawDepthNormalPrepass | 14.54 / 9.57 | 14.55 / 9.57 |
| whole frame | 37.98 | 35.13 |

- At quarter, the march is a quarter of half's cost, as it should be. Low-res depth and resolve are under 0.12 ms.
- **The biggest pass that doesn't scale is DrawSkybox: 3.7 ms at both sizes**, more than the march even at half.
  It's one full-resolution draw of the skybox mesh. Neither skipping the sky colour (`-skyoff 4`) nor skipping the far
  clouds (`2`) makes it cheaper; only `-benchnosky` removes it. So it's the skybox shader's own per-pixel work
  across the whole screen (looking up 10 at the Summit, most of the screen is sky).
- No cloud-shadow pass shows at this depth. The sky's cost is the march plus this one skybox draw.
- So quarter can only save the march's share. To go further, the skybox draw itself needs to be cheaper or moved
  into the low-res buffer.

Captures in `results/rd68`, all passes in `results/q68-passes.txt`, shots in `results/q68-shots` (laptop).

**Freeze: 4 of these 24 games stalled, and the game is not responding during a stall.** All four stalled just after
the jump to the Summit (settling): 527 s (half-off2 r1), 1624 s (half-off4 r1), 112 s (quarter-off4 r1) and 490 s
(half-off4 r2). The 83 snapshots in `results/freezestall.txt` all look the same:
- Windows reports the game as not responding (its window isn't pumping messages).
- About 40 threads wait on UserRequest (an object or event wait) and about 30 on an unknown wait.
- One thread uses about 0.9-1.1 s of CPU per 30-40 s (about 3%), the same thread through each stall; nothing else runs.
- I/O is flat, the disk is 0% busy, the CPU is about 5% overall and Defender shows no CPU.

So **the game is blocked in a wait, with one thread polling slowly**. It isn't busy, isn't loading from disk, and
isn't stopped by Defender. Each snapshot now also names the main thread's state (b7224267), so the next one will
say whether it's the main thread that waits and whether the polling thread is the main thread.
Odd: all four stalled games had `-skyoff 2` or `4` (8 of the 24 games did; 0 of the other 16 stalled). By chance
alone that's under 1%. But job 37's 728 s stall had no `-skyoff` at all, so these flags don't cause it. They may make it
more likely. Every game here jumps to the Summit, so this job says nothing about the place.

Cost: this job about $3 (laptop session 0a48ccdb); laptop total $668.

## Job 37: R-2 on the laptop (main acd0cad5 vs 13cdbcdc), 2026-11-09 (q67.sh)
Profile builds, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, a fresh game per run, ABBA (two rounds each,
within 0.6 ms). The runner's cpu column is the main thread's frame time.

**(1) World glow bake: 11.8-12.4 s on the laptop** (11754, 11819, 11924, 11955, 12426 ms; 2.9x the main PC's 4.1 s).
Same counts each game: 6366 burning trees, 2816 glowing plants, 2 MB, 9216 m. A sixth game logged 729311 ms: that
game stalled for 728 s mid-bake (see the freeze below), so it's not the bake's own time.
`-worldglow 0` still runs the bake (it zeroes the strength only).

**(2) Fire test** (job 27's spots: firesbase, then the same with `-benchfires 500`, noon; 500 flames drawn, 0 dropped):

| | GPU firesbase | GPU fires | main thread firesbase | main thread fires |
|---|---|---|---|---|
| 13cdbcdc (before R-2) | 15.85 | 16.15 | 18.45 | 18.7 |
| acd0cad5 (R-2) | **13.8** | **14.1** | **17.05** | **17.15** |

- The 500 flames cost 0.3 ms GPU on both builds (0.1-0.25 ms main thread). R-2 doesn't change the flames' cost.
- R-2 is 2.05 ms GPU and 1.4 ms main thread cheaper at this spot, **with or without the flames**. That's the rest of
  R-2 (the detail rules), not the fires.

**(3) Ember Forest at night** (job 27's view: `-benchview 120,5 -benchhour 22`; 3296 flames a frame), acd0cad5:
default 10.25 ms GPU / 13.2 main thread, `-worldglow 0` 10.1 / 13.15. **The world glow is free at draw time** (0.15 ms,
within noise).

**Freeze: caught one with the sampler, and the game was idle, not busy.** The first acd0cad5 game (fires r2, round 1)
stopped logging at 08:45:44 for 728 s. Every 6 s through the stall:
- the game's CPU time rose 0.03 s;
- GPU load was 0% at idle clocks (1063 MHz);
- its working set shrank (Windows trimming an idle process).

So it isn't a CPU loop and isn't the GPU: **something blocked the game** (a lock, a wait, or I/O). No events in the
System, Application or Defender logs from 08:40 to 09:00. It then carried on and measured normally.
The sampler now snapshots a stall: its threads by state and wait reason, which threads used CPU, the game's I/O, and
disk and CPU load (`freezestall.ps1`; results/freezestall.txt). The next stall should name the wait.
Since job 32 the stalls have hit just after the jump (settling), on main and branches alike, Summit, lake and now job
25's start spot; job 36's 34 games had none.

Cost: this job about $2 (laptop session 0a48ccdb); laptop total $667.

## Job 36: clouds on the low-resolution buffer (sky/lowres ccd74cc1 vs main 13cdbcdc), 2026-11-09 (q66.sh)
Job 34's setup: Profile builds, the Summit facing out, pitched up 10, 1920x1080, `-gfx High`, `-weatherseed 4`,
uncapped, noon, a fresh game per run, weather forced over the stop. ABBA, two rounds each. Rounds agreed within 0.9 ms.
Main has neither sky/cost's strides nor the branch's fewer far sun samples, so "full" carries both.

| plain weather | GPU ms | CPU ms | vs main |
|---|---|---|---|
| main | 22.25 | 24.45 | |
| branch `-skyfull` | 18.85 | 21.3 | -3.4 (strides + far sun samples; job 34 said -3.4 for strides alone) |
| `-lowres half` | 9.3 | 14.1 | -12.95 |
| `-lowres quarter` | 9.1 | 12.9 | -13.15 |
| default (High) | 9.25 | 14.15 | -13.0 (same as half: High picks half) |

| weather, default size | main | default | saves |
|---|---|---|---|
| Clear | 20.8 | 9.1 | 11.7 |
| Overcast | 21.9 | 9.5 | 12.4 |
| Storm | 25.0 | 11.45 | 13.55 |

- **Half resolution takes the Summit from 22.25 to 9.25 ms GPU (-58%).** Job 34's no-sky frame was 6.7 ms, so the sky
  is now about 2.5 ms, down from about 15.5.
- **Quarter buys almost nothing more** in plain weather (9.1 vs 9.3). It does in Storm: one run each gave half 12.1,
  quarter 10.0 (weather volumes still cost at half). Full 24.2 and main 24.9 in that Storm view.
- **CPU drops too** (24.45 to 14.15 ms). The runner's cpu column is frame time on the main thread, which waits on the GPU
  here, so it follows the GPU; this isn't a CPU saving.

**Look** (one shot each, main / full / half / quarter, in the laptop's `results/q66-shots/`):
- Tree line (the dense forest spot, looking up 15): at full resolution **half shows no halo** at the leaf edges.
  **Quarter has a bright cloud patch hugging the leaf silhouette** where main and half show thin wisps. It may be a
  real cloud (each run is a fresh game, and the shots are a few seconds apart), or the low-resolution sky leaking past
  the edge. Worth a look on the desktop before quarter is used below High.
- Horizon (the Summit, level): no halo at either size; the small cloud is softer at quarter.
- Storm: the four look the same at this size (snow and haze hide the clouds).
- Timed in passing, one run each: tree line 14.2-14.7 ms in all four (little sky in view), horizon main 18.1 / full
  15.3 / half 9.0 / quarter 9.0.

**Freeze: none in these 34 games**, Summit included, on main 13cdbcdc and the branch. Job 34 had 12 in 37 on main
4669aabc and sky/cost. Since then main gained the water overhaul, the lights manager and the flora class rules
(#208-#211). The freeze sampler ran throughout (495 samples, no stall to see). Not proof, but if it stays clear the
cause may be gone with one of those merges; I'll keep the sampler on for the next Summit and lake runs.

Cost: this job about $4 (laptop session 0a48ccdb); laptop total $665.

## Job 35: can the depth-normal prepass go? (render/ssao-source 75df5e08), 2026-11-08 (q65.sh)
Profile build, job 31's bigtown stop and dense forest spot (1740,3355,180) in one game per run, 1920x1080, `-gfx High`,
`-weatherseed 4`, uncapped, noon. Order normals depth after off off after depth normals; the log confirms each mode
("SsaoTest: <mode> on PC_Renderer"). GPU ms, mean of 2 rounds (rounds within 0.4 ms):

| `-ssao` | bigtown | forest |
|---|---|---|
| normals (today) | 17.2 | 14.35 |
| depth | 17.45 | 14.4 |
| after | 17.4 | 14.1 |
| off | **15.85** | **11.85** |

**`depth` and `after` save nothing. Only `off` does: 1.35 ms at bigtown, 2.5 ms in the forest.**

**Why: the prepass isn't SSAO's.** RenderDoc, one capture per mode at the forest (RenderDoc's serialized timings run
about 3x the real frame; shares are what count):

| mode | prepass | its time | SSAO | frame |
|---|---|---|---|---|
| normals | DrawDepthNormalPrepass | 14.56 | 1.82 | 44.07 |
| depth | Draw Depth Only | 13.54 | 1.81 | 43.06 |
| after | Draw Depth Only | 13.50 | 1.84 | 43.05 |
| off | Draw Depth Only | 13.52 | none | 41.52 |

- With SSAO off a depth-only prepass still draws the same 1255 draws at 13.5 ms (33% of the frame). `after` didn't
  remove it either.
- What keeps it alive: **`PC_Renderer.asset` has `m_DepthPrimingMode: 2` (Forced)**, with Forward+
  (`m_RenderingMode: 2`). Forced depth priming lays depth down first every frame, and the opaque pass then shades only
  the front surface. SSAO's normals ride on that prepass for 1.0 ms extra (14.56 vs 13.54). Job 31's "only SSAO uses
  it" was wrong: I read the pass name, not the renderer setting.
- So SSAO's real cost is about 1.8 ms (its pass) plus 1.0 ms (normals in the prepass) in RenderDoc terms, which is the
  1.35-2.5 ms `off` saves in the timed runs.
- **The open question is the prepass itself:** priming Forced vs Disabled. Without the prepass the opaque pass shades
  overdraw (the forest is mostly overlapping leaves), so it could cost more than it saves. Job 31 showed DrawOpaqueObjects
  at about 20 ms here *with* priming. Suggest a `-depthpriming off|auto|forced` test flag; then the same two spots, and
  RenderDoc at the forest.

**Look:** shots of each mode at both spots are in the laptop's `results/q65-shots/<mode>/`. At a glance `off` is a bit
flatter under the trees and around the cobbles; normals, depth and after are hard to tell apart at a glance.

Freeze watch: none in these 8 games plus 4 captures (bigtown and forest; the stalls have been at the Summit and the
lake). The new sampler (`freezesample.sh`, every 5 s: the game's CPU time, GPU load, log growth) ran throughout.

Cost: this job about $1 (laptop session 0a48ccdb); laptop total $661.

## Job 34: the cloud march's empty-air strides (sky/cost 79ffa7c9 vs main 4669aabc), 2026-11-08 (q64.sh)
Profile builds, the Summit place facing out, pitched up 10, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, noon,
a fresh game per run, weather forced over the stop by the runner. Two rounds each (ABBA) unless marked x1; GPU ms:

| Weather | main | branch `-skyoff 16` (old steps) | branch `-skyoff 0` (new) | new vs main | new vs old steps |
|---|---|---|---|---|---|
| plain | 23.5 | 25.9 | **20.1** | -3.4 | -5.8 |
| Clear | | 24.5 | **18.55** | | -5.95 |
| Overcast | | 26.1 | **20.35** | | -5.75 |
| Storm | | 27.35 | **25.0** | | -2.35 |
| no sky (`-benchnosky`, x1) | | 6.7 | | | |

Rounds agreed within 0.5 ms except Storm old (28.3 / 26.4). Main was timed in plain weather only.
- **The new path saves 3.4 ms GPU against main** at the Summit (23.5 to 20.1, 14%). The sky, taken as frame minus the
  no-sky baseline, goes from about 16.8 to 13.4 ms.
- **`-skyoff 16` is not main.** The branch's old even stepping is 2.4 ms slower than main itself (25.9 vs 23.5), so
  the "-5.8 vs old steps" column overstates the win. Likely the old stepping without the 160-step cap (rays now go
  further), but that's a guess. Main against the branch is the number to quote.
- Storm gains least (-2.35): its sky is mostly weather volume and cloud, not empty air.

**What's left once the march is cheaper** (plain, branch, new steps, GPU ms, against 20.1):

| `-skyoff` | GPU | saves |
|---|---|---|
| 1 (no cloud march) | 8.6 | 11.5 |
| 2 (no far clouds) | 19.75 | 0.35 |
| 4 (no sky colour) | 18.9 | 1.2 (rounds 20.3 / 17.5, noisy) |
| 8 (no weather volumes) | 19.35 | 0.75 |

The cloud march is still 11.5 of the sky's 13.4 ms. Far clouds, sky colour and weather volumes are under 1.5 ms each.
Next target is still inside the march (cost per step, or fewer steps inside cloud), not the other parts.

**Sunset** (`-benchhour 17.6`, sun about 6 degrees up), same view: old steps 24.75, new 20.5 (x2 each). Facing west
into the sun (yaw 270, pitch -5, x1 each): main 23.3, old steps 24.5, new 23.8. **Facing the low sun the new path is
no faster than main** (one round each; worth a second look if sunsets matter). Full-resolution crops of the sunset sky
show **no banding** on either path. The new path adds a faint grain at cloud edges (the per-pixel stride variation);
easy to miss at 1:1.

**Steps per pixel** (`-skyoff 32`, shots): old steps are red over the whole sky; new steps are yellow-green with green
around cloud. Noon and sunset look the same. Shots are in the laptop's `results/q64-shots/` (no picture hosting).

**Freeze: the display explanation is wrong.** 12 of 37 games here froze at the Summit, for 112 to 1586 s, on one
frame 0.5-8.5 s into settling. The game's own Net status lines stop for the same span (947 s, 931 s, ...), so it's a
real stall, not a timer glitch. Main, old steps and new steps all froze; the one no-sky run didn't (one sample). The
keepawake helper was running (checked: it holds the display on while the bench lock is held), and the System log has
no power, display or driver events in the whole 19:18-22:46 window. So 8078cce6's "display turning off" verdict is
withdrawn too. The numbers above are unaffected (the runner measures after settling); the freezes cost hours.
Next on the laptop: sample the game's CPU time and the GPU's load every few seconds during Summit runs, so a stall
shows whether the game is busy (a CPU loop), waiting on the GPU, or idle. Still no stack: open to suggestions.

Cost: this job about $3 (laptop session 0a48ccdb); laptop total $660.

## 2026-11-08 Job 33 part 3: lava plates and sea swells cost nothing measurable, but the plates may not be drawing (q63.sh lava, q61.sh swells)

Profile builds, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, noon, a fresh game per run, ABBA. **0 frames over 5 s in the last ~50 min of unattended runs (16:48-17:13), after the keepawake fix** (see the correction below).

### Lava plates (water/lava-plates 53582b9b against main 46012623)

- **The view:** a shot-list scout of 17 views (q63.sh scout) picked 7 m north-east of the MagmaSerpent (-206,-2124), facing north-east, pitch 75. Lava fills about half the frame. The runner's own spot stop there put the camera in a rock face, so these runs use shot-list sessions with `-perflog`: 40 s fill, then 4 lines of 6 s, the same view each line.
- **GPU ms, mean of 4 lines:** main 6.33 / 6.27, plates 6.32 / 6.35 (two rounds each, second round 2 h after the first round's 6.39 / 6.40). **No cost.** Main thread 10.1-10.3 ms both.
- **But the shots of main and plates look the same:** flat orange, no plates or glowing melt (`results/q63-lavashots/q63-lava-*-1/s4.png`). Either the plate pattern is too fine to show from ~15 m at High, or the branch's lava shader isn't on this crater's lava (e.g. only on lava rivers or another material). **Worth a look on the branch before reading "no cost" as the answer.**

### Swells at sea (water/swells 098ddffd against main and `-noswells`)

- **The view:** the bench's Beach place (400,300) turned out to be a sandy canyon with an inlet at every yaw (q63.sh beachscout), so every earlier "beach" number was measured with no open sea in view. These runs stand at FishingVillage (391,-663), wading at sea level, facing south (`-benchview 180,5`), with water over the lower half of the frame and the far shore beyond. The storm shot shows a breaker.
- **GPU ms, mean of 2 rounds:**

| Weather | Main | Swells | `-noswells` | Δ swells vs main |
|---|---|---|---|---|
| Clear | 21.95 | 22.15 | 21.8 | +0.2 (noise) |
| Storm | 24.45 | 24.95 | 24.1 | +0.5 (noise-sized; rounds 23.8-25.1) |

- **No measurable cost.** Main thread 13-15.9 ms, the same across variants. The Clear set was rerun at 17:00 after two runs froze at 16:00 (the display; see the correction).

### Job 33 done, except `water/river-flow` (not pushed yet)

| Branch | Cost |
|---|---|
| grass/clumps | **+1.1 ms GPU** (meadow, full screen) |
| night/blue-moon | +0.2 (noise) |
| refactor/pose-by-size | not cheaper (CPU; see part 1) |
| water/glitter-grid | 0 |
| water/swells | 0 at the lake, +0.2 / +0.5 at sea (noise) |
| water/lava-plates | 0, but see above: the plates may not be drawing |

Cost: laptop CSV total $657.

## 2026-11-08 Correction to the freeze verdict: it froze in daylight, and the likely cause is the display turning off (bench fix pushed)

- **This morning's verdict ("the laptop overnight") was wrong.** Job 33's FishingVillage beach runs froze at 16:00 (a 35 s frame) and from 16:01:33 to 16:27:48 (**one 1573 s frame**, nothing logged in between). Builds: water/swells 098ddffd built with today's runner (m4). So it's not that runner's age, not main's code, and not the hour.
- **What the freezes have in common: nobody was using the laptop.** Overnight, and 16:00-16:27 this afternoon. The clean runs (q62 at 10:49-11:15, the 15 Summit sessions) were while someone was.
- **The bench's keepawake helper didn't hold the display for the q-scripts.** It sets `ES_DISPLAY_REQUIRED`, but it only stays alive while a Punchy game runs or bash runs `florabatch|benchrun|bench.sh`. The queue scripts (q5x/q6x.sh) never matched, so the helper quit at the first 30 s check between two games. After that, the display turned off at its 15 min idle timeout (read with powercfg, not changed) and the game's frame blocked until someone came back. Windows' event logs show nothing either way.
- **Fix** (b/bench-stress ebd22ef5): keepawake also holds while the bench lock (`results/.lock`) exists, which is the whole of any queue script. No power settings changed.
- **Not proven yet:** I haven't turned the display off on purpose to reproduce it (that's a power setting). The next unattended runs are the test. If a frame over 5 s shows up again, this is wrong and I'll say so.
- **Data affected:** the beach Clear round 2 runs (rerunning now). Every frozen run so far was either rerun or flagged as void.

## 2026-11-08 Job 32 (refactor 3.4): the sky costs 15-19 ms GPU at the Summit; Snell's window saves 23-29 ms under water (q60.sh, third try)

- **Builds:** Profile builds of main 46012623 (m3: today's runner, with `-benchnosky`) and water/snells-window 31b890be.
- **Runs:** 1920x1080, render scale 1, `-gfx High`, `-weatherseed 4`, uncapped, noon, a fresh game per run, 12:40-14:30. 2 rounds each, the second in reverse order. 0 freezes, 0 crashes, 0 exceptions.
- **Why a third try:** `-benchnoshader Sky` never removed the sky (it's the scene's skybox material, not a renderer), so the earlier "no sky" runs were void. `-benchnosky` sets the skybox to none and clears to a flat colour; the log confirms "off skybox Sky (Punchy/Sky)" and the shot shows a flat blue with no clouds. The second try's native crash (dungeon generation, overcast round 2) didn't recur.

### A. Sky by weather: the Summit, facing out, camera pitched up 10° (sky ~2/3 of the screen)

| Weather | GPU ms | Sky cost (− no sky) | CPU ms | fps | 1% low |
|---|---|---|---|---|---|
| No sky (`-benchnosky`) | 6.8 | 0 | 10.8 | 92.3 | 70.0 |
| Clear (forced) | 21.5 | **14.7** | 23.1 | 43.4 | 29.9 |
| Plain (seed 4's own: partly cloudy) | 23.1 | **16.3** | 24.1 | 41.5 | 30.9 |
| Overcast (forced) | 23.2 | **16.4** | 24.8 | 40.5 | 28.5 |
| Storm (forced) | 25.6 | **18.8** | 26.8 | 37.3 | 25.2 |

- **The sky is ~70% of this frame's GPU time, and most of it is there even in clear weather.** Clouds add only 1.6-4.1 ms on top of a clear sky's 14.7 ms. So fewer clouds (Zach's question) is a bit cheaper, but clearer weather alone won't fix it: the base per-pixel cost of the sky shader is the problem, not the cloud count.
- **RenderDoc** (one capture each, replay times ~3x inflated): the skybox draw is one full-screen draw of 22.0 ms (Clear), 23.5 ms (plain), 24.1 ms (Overcast) of 51-53 ms frames: 43-45%. The same order as the live runs.
- CPU ms rises with the GPU because the main thread waits on the GPU; it's not CPU work in the sky.
- **Ways to cut it** (for the rendering lane): render the sky at half or quarter resolution and upsample (the clouds are soft); stop the march early on a clear ray (the early out only fires once a cloud turns opaque); fewer steps toward the horizon; or cache the sky into a cubemap and refresh it a few faces a frame, since it changes slowly.

### B. Under water: Snell's window + the sky skip

| Spot | Main GPU | Snell GPU | Δ GPU | Main fps / 1% low | Snell fps / 1% low |
|---|---|---|---|---|---|
| Job 29's lake spot, under water (pitch −35, looking up) | 34.5 | 11.5 | **−23.0** | 26.2 / 14.7 | 62.2 / 25.1 |
| 12 m east of the pillar | 39.5 | 10.8 | **−28.7** | 23.5 / 12.6 | 64.8 / 26.2 |

- **This is the leftover underwater cost from jobs 29-31: it was the sky, drawn behind the water's underside.** The branch returns the fog colour for sky pixels under water, and the frame drops to ~11 ms. Slow frames (>33 ms) go from ~340 to ~6 per run.
- **It looks different too:** main shows a fogged, flat ceiling; the branch shows the rippled surface from below with the bright window above (shots `results/q60-shots/under-main` and `under-snell`). Whether that's the intended look is for Zach; the cost says merge.

Cost: laptop CSV total $653.

## 2026-11-08 Freeze A/B (q62.sh) and Job 33 part 2: clumps, glitter, swells at the lake (q61.sh)

### The freeze: not the runner, not main's code; it hasn't come back since ~09:00

- **Runs (10:49-11:15):** job 29's lake spot (-1624,-488), where main froze 2 of 2 overnight. Profile builds, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, noon, a fresh game each.
  - `m`: main 46012623 built 04:18 with the runner of then (the build that froze overnight).
  - `m2`: the same main built 10:11 with today's runner.
  - `j30`: job 30's 346cf4d9 with the same runner as `m`.
  - Each with the bench route's spot stop and (m, m2) without the route at all: `-goto -1624,-488` and a 90 s shot list, as the player lane tried. Order m/m2/j30 route, m/m2 goto, then reversed.
- **Result: 0 freezes in 10 sessions.** The longest gap between the game's own "Net status" lines (logged every ~10 s) is 17-18 s, at load, in every session; none of the runner's ">5 s frame" lines. The spot stop reads the same in all 6 route runs: 34.6-36.2 fps, GPU 26.5-27.1 ms, worst frame 46-82 ms.
- With the 15 clean Summit sessions since 09:00 (m2, m3 rerun of job 32, the freeze hunt), **nothing has frozen since ~09:00 on any build, the 04:18 build included.** Every freeze was between ~04:20 and ~08:40.
- **So it's the laptop in those hours, not main's code, not the bench runner and not an edit on this machine** (the build checkout is clean; the runner is committed, 8cb2b066; the `m` build froze then and runs clean now). Windows' System, Application and Defender logs show nothing in those windows; something scheduled overnight (an update scan, indexing, a driver) stalling the GPU or the disk is the best guess. Matches the other computer's main not freezing.
- **What I'll do:** the bench runner keeps logging any frame over 5 s and caps settling at 0.25 s a frame, so a freeze can't eat a stop silently again. If one shows up tonight I'll note the time and look again at the event logs (read only).

### Job 33 part 2

GPU and CPU ms from the Profile timer, mean of 2 rounds (ABBA); main / render thread ms from the runner.

| Branch | Where | Main | Branch | Δ GPU | Δ main thread |
|---|---|---|---|---|---|
| `grass/clumps` d6fee299 (vs `-grassclumps 0`, same build) | Meadow, `-place meadow`'s own finder | GPU 11.3, CPU 17.4, 57.8 fps | GPU 12.4, CPU 18.4, 54.4 fps | **+1.1 ms** | +0.55 (13.65 → 14.2) |
| `water/glitter-grid` 17c1b431 | job 29's lake shore | GPU 26.9, 35.6 fps | GPU 26.5, 35.8 fps | −0.4 (noise) | 0 |
| `water/swells` 098ddffd | job 29's lake shore | GPU 26.8, 35.9 fps | GPU 26.9 (−noswells 27.0), 35.5 fps | +0.1 (noise) | +0.4 |

- **Clumps cost +1.1 ms GPU** in a full-screen meadow (over the +0.5 ms flag) and slightly lower 1% lows (20.9 vs 25.1 fps). RenderDoc, one capture each: opaque pass 19.8 vs 19.3 ms, depth-normal prepass 12.2 vs 11.7 ms (replay times, ~3x inflated), so the extra cost is split about evenly between the two passes that draw the grass; the draw counts are the same (941 vs 974). Shot: `results/q61-shots/clumps-clumps/place.png` looks right (field and the deer in frame).
  - A cheaper clump would skip the depth-normal prepass for grass (it draws everything opaque twice, see job 31) or use fewer blades per tuft at range.
- **Glitter grid: no cost**, as expected.
- **Swells at the lake: no cost** (lakes get only the new still shore line).

### Still void, rerunning

- **Swells at the beach:** void again. The Beach place faces inland: the shot is a sand dune face with the sea only at the bottom-left corner, so Clear and Storm read the same (76.5-77.2 fps, GPU 10.6-10.9 ms, all variants). Next try turns the camera 180° from the place's yaw (runner: `-benchview place+180,8`) and checks the shot before the timed runs.
- **Lava plates:** void. The spot 45 m south of the MagmaSerpent lands among the rim boulders; the shot is rocks with a sliver of lava in one corner (main 101 fps / GPU 4.7 ms, plates 96.7 / 5.0 ms: +0.3 ms on almost no lava pixels). Next try picks the spot from a few candidates by their shots first.
- **`water/river-flow`:** not pushed yet; I'll add it when it is.
- **Job 32's third run (sky by weather with `-benchnosky`, Snell's window)** starts now.

Cost: laptop CSV total $652.

## 2026-11-08 Job 31 (refactor 3.5c): close-up shadow range 25 / 50 / 75 m, static vs animated (q59.sh; b/bench-stress 4b721ea4 for review)

- **Build:** Profile build of refactor/flora-static 5fa346f3 (rebuilt so the bigtown stop takes `-benchview`).
- **Runs:**
  - 1920x1080 set explicitly, render scale 1, `-gfx High` (view 1.3, detail 1.5, shadows 1.4, 4 cascades), `-weatherseed 4`, uncapped, noon.
  - Spots: bigtown (2972,-885) and job 25's dense forest (spot 1740,3355).
  - `-shadownear 25 / 50 / 75`, each static and animated (`-nostaticflora`), 2 rounds, the second in reverse order.
  - Each round also has one `-benchnoshadows` run per variant. The shadow pass = GPU minus GPU without shadows.
  - Then one Profiler capture per range, variant and spot (SysMs).
  - 32 sessions (16 runs, 12 captures, 4 view runs whose shots were lost to a missing folder), 0 crashes, 0 exceptions.

### What `-shadownear` actually changes at High

- Trees cast real-time shadows to `min(Trees.ShadowCastDistance 60, URP shadow distance 50 x 1.4 = 70, NearRange)`. Shrubs stop at their own 40 m.
- **So `-shadownear 75` is really 60 m for trees.** 50 → 75 adds only the trees from 50 to 60 m.
- **A gap at 75:** the far cache starts at NearRange − 12 m = 63 m, but real-time tree shadows end at 60 m. Trees 60-63 m out would cast no shadow at all.
- **To widen past ~60 m** (and past 70 m at High), `Trees.ShadowCastDistance` and the URP shadow distance have to rise with it.

### Results (GPU ms, Profile timer; mean of 2 rounds)

| Spot | Range | GPU static | GPU animated | Shadow pass static / animated | Frame median | Render thread | Main |
|---|---|---|---|---|---|---|---|
| Big settlement | 25 | 17.25 | 17.5 | 0.65 / 0.6 | 20.3 / 20.25 | 6.7 / 6.7 | 11.25 / 10.9 |
| Big settlement | 50 | 17.75 | 17.8 | 1.15 / 0.9 | 20.45 / 20.5 | 6.6 / 6.7 | 10.85 / 11.1 |
| Big settlement | 75 (= 60) | 17.55 | 17.5 | 0.95 / 0.6 | 20.5 / 20.5 | 6.85 / 6.95 | 11.15 / 11.3 |
| Big settlement | no shadows | 16.6 | 16.9 | | 19.5 / 19.55 | 5.55 / 5.5 | |
| Dense forest | 25 | 14.9 | 14.7 | 2.7 / 2.45 | 17.05 / 17.1 | 5.45 / 5.45 | 10.95 / 10.9 |
| Dense forest | 50 | 15.45 | 15.35 | 3.25 / 3.1 | 17.75 / 17.8 | 5.35 / 5.3 | 10.95 / 10.95 |
| Dense forest | 75 (= 60) | 15.05 | 15.15 | 2.85 / 2.9 | 17.8 / 17.8 | 5.3 / 5.35 | 11.2 / 11.2 |
| Dense forest | no shadows | 12.2 | 12.25 | | 15.6 / 15.5 | 5.3 / 5.25 | |

- **25 → 50 m costs about +0.5 ms GPU** at both spots, static or animated. That's +0.3-0.5 ms on the shadow pass and +0.15-0.7 ms frame median.
- **50 → 75 m (really 60) measures nothing further:** GPU at 75 is at or below 50 at both spots (0.2-0.4 ms lower), inside the round-to-round spread.
- **The static variant doesn't win back shadow cost:** static and animated are within 0.25 ms of each other at every range.
- **Whole shadow cost (25 m):** 0.6 ms GPU in the town and 2.5-2.7 ms in the forest, plus 1.1-1.2 ms on the render thread in the town (6.7 vs 5.5).

### From the captures (one per range and variant)

| Spot | Range | Shadow casters (Unity counter) | URP shadows CPU ms, all threads / main | SetPass static / animated |
|---|---|---|---|---|
| Big settlement | 25 | 746 | 3.54 / 0.10 (animated 3.42) | 470 / 447 |
| Big settlement | 50 | 786 | 3.76 / 0.10 (3.59) | 468 / 451 |
| Big settlement | 75 | 802 | 3.74 / 0.10 (3.66) | 468 / 451 |
| Dense forest | 25 / 50 / 75 | 327 | 1.65-1.70 / 0.09 | 447 / 434 |

- Unity's shadow caster counter doesn't see the flora's indirect shadow draws (the forest stays at 327 at every range). The town's +40 / +56 casters are the kit and props that come inside the wider shadow range.
- URP's shadow work is on the render and worker threads: +0.2 ms in the town at 50 m, flat in the forest.
- Static adds 13-23 SetPass calls (as job 28 found).

### Shots (bigtown, 17:00, static, `-benchview <yaw>,8`, one per yaw and range: `results/q59-shots/y<yaw>-near<n>/`)

- **The street view is yaw 180:** it looks down the road that leaves the square, with houses on both sides and towers on the left. Yaw 0 and yaw 90 face across the square to the houses; yaw 270 faces into the sun.
- **At this view, 25 and 75 m look the same.** The near shadows of houses and props at 20-60 m are already drawn by the far shadow cache at 25 m, and nothing in frame changes between ranges. What the wider range adds isn't visible from the square at 17:00. A view down a tree-lined path, or the forest spot, would be the place to judge it.

### Per-pass GPU split from RenderDoc (q59r.sh, rdpasses.py)

- **Captures:** 7 frames.
  - Town: bigtown, 25 m, ×3.
  - Forest: spot 1740,3355, 25 m, ×3.
  - Under and shore: job 29's lake view, on 346cf4d, ×1 each.
- **Method:** each event's GPU duration (`EventGPUDuration`, median of 3 replays), summed under Unity's pass markers.
- **Replay inflates every draw.** Totals read 44.6 ms (forest), 53.0 (town), 69.8 (shore) and 63.7 (under), against 13.9 / 16.5 / 23.2 / 32.9 ms in the runs: about 3x, and 1.9x under water, where there are few draws. So the table gives each pass's share of the replay frame. The 3 captures per spot agree within 1%.

| Pass | Forest | Town | Shore | Under water |
|---|---|---|---|---|
| Opaque (`DrawOpaqueObjects`) | 46% (flora indirect 23%, SRP-batched terrain/props 23%) | 52% (batched 37%, indirect 15%) | 37% | 12% |
| Depth-normal prepass | **33%** | 22% | 16% | 7% |
| Main light shadow map | 8% (326 draws, 296 indirect) | 4% (747 draws, 688 indirect) | 2% | 1% |
| Skybox (1 draw) | 3% | **13%** | 6% | **48%** (30.8 ms in replay) |
| Transparent (water etc.) | 2% | 2% | **34%** | 29% |
| SSAO, bloom, the rest | under 2% | under 2% | under 2% | under 2% |

- **The depth-normal prepass costs two-thirds as much as the opaque pass itself** in the forest (33% against 46%), and 22% in the town. Everything opaque is drawn twice, the second time for its normals (SSAO, SSR). That's the biggest single target these captures show, bigger than shadows. A depth-only prepass (no normals), or dropping it where SSAO/SSR aren't needed, would be worth a job.
- **The shadow map is 4-8%** in these captures. That agrees with the 25 m shadow pass from subtraction above (0.6 ms town, 2.5-2.7 ms forest), allowing for replay inflating the many small indirect shadow draws differently.
- **The sky is one draw, and pixel-heavy.**
  - 13% of the town frame (wide open sky at bigtown).
  - **48% of the underwater frame:** the camera looks up through the surface and the sky shader runs behind the water on every pixel.
  - This answers job 30's open question (the underwater view still read 27 ms with water, terrain, flora and grass off): it's the sky. Job 32's rerun measures it properly with `-benchnosky` (a removed skybox: `-benchnoshader Sky` turned out not to remove it, see Job 32).
- Top single draws (in `results/q59r-passes.txt`):
  - Forest: indirect flora draws at 0.25 ms each (4833 indices x 109-113 instances).
  - Shore: the water surface.
  - Under water: the skybox.

**Cost:** laptop session 0a48ccdb, 2026-11-07 and 08: $35 equivalent so far across jobs 28-33 (`COSTS/laptop.csv`).

## 2026-11-08 Freeze, update: runner, uncommitted edits, or the laptop? An A/B is queued (q62.sh)

On the suspicion that it's the bench runner or uncommitted edits on this laptop:

- **The build checkout is clean.** `~/Projects/Punchy` has only two untracked files, `.vsconfig` and `Assets/Resources.meta` (dated Oct 27 and 29).
  - A build adds exactly `BenchmarkRoute.cs` and `BProfileTools.cs` from `b/bench-stress` and removes them afterwards. `BProfileTools.BuildDev` is a plain `BuildOptions.Development` build with no profiler autoconnect.
  - So the runner, which does carry uncommitted edits, is the only thing these builds add to main.
- **Which runner each build carries** (fingerprinted from the log strings in its `Assembly-CSharp.dll`):
  - main `m` (04:18) and job 30's `346cf4dl` (03:06) carry the same runner.
  - `346cf4dl` ran the lake spot cleanly at 03:15. `m` froze there and at the Summit.
  - `m2` (10:11, today's runner) has run 8 Summit sessions since 10:15 without a freeze, including the 4 tries meant to catch it with a Profiler capture.
- **Freezes aren't tied to the jump.** The glitter build logged a 135 s frame while settling and a 447 s frame 19 s into the measured part (frame 622).
- **Freezes so far all fell between about 04:20 and 08:40.** Nothing froze from about 09:00 on (pose, blue moon, freeze hunt), on any build.
  - The Windows System, Application and Defender logs show nothing for those windows: no power or display events and no scans.
- **The A/B (q62.sh, next after the Job 32 rerun):** at the lake spot, a fresh game per run, two rounds:
  - main `m` (the build that froze) and `m2`, each with the bench route and without it (`-goto -1624,-488` plus a 90 s shot list);
  - `346cf4dl` with the route.
  - A freeze reads from gaps between the game's own "Net status" lines, so it's measured the same way with or without the runner.
  - If nothing freezes in daylight, it's the laptop overnight, not the code. If only `m` freezes, it's the runner of 04:18, and today's runner has already fixed it.
- **Lava, third try:** VolcanoCone isn't a DebugControls place on this island at all. It now uses a spot 45 m south of the MagmaSerpent on the crater floor (-211,-2129), looking down.

## 2026-11-08 Job 33 (part 1): blue moon and pose-by-size; swells voided (q61.sh)

Profile builds, 1920x1080, render scale 1, `-gfx High`, `-weatherseed 4`, uncapped, a fresh game per run.

### Blue moon (night/blue-moon 7be628b2 against main 46012623, M B B M)

- **Setup:** the Summit at hour 0, Clear forced, `-bluemoondays 0`. The camera is aimed at the blue moon from `DayNightCycle.BlueMoonDirection` and re-aimed once the hour is set.
  - The shot (`results/q61-shots/bluemoon-blue/place.png`) has the moon large in frame, its glow and the small white moon beside it. The player is partly in front.
- **Cost:** GPU main 21.9 / 21.5 ms, blue 21.9 / 21.8. Frame median 22.4 / 22.1 against 22.4 / 22.4. 1% low 34.8 / 34.1 fps against 34.5 / 34.9.
  - **So the blue moon costs at most +0.2 ms GPU, inside the round-to-round spread.**
- These 4 Summit runs (m2 builds) didn't freeze. No frame over 5 s was logged.

### Pose by size (refactor/pose-by-size dcd0318d against main, `-perflog` shot-list sessions, A B B A)

Pose job median ms (median of the 5 perflog lines) and creatures posed per frame:

| Spot | main r1 | size r1 | size r2 | main r2 |
|---|---|---|---|---|
| Meadow (`-wildcap 2000`) | 7.67 ms, 140 posed | 7.73 ms, 133 | 8.90 ms, 157 | 7.71 ms, 136 |
| Mountains | 2.33 ms, 31 posed | 2.68 ms, 41 | 2.91 ms, 44 | 2.63 ms, 38 |

- **Pose by size is not cheaper.**
  - Mountains: +0.3 ms on the median, posing 41-44 creatures a frame against 31-38.
  - Meadow: about level in round 1, +1.2 ms in round 2 (157 posed against 136).
- Pose job worst frames are about the same: 4-11 ms on both.
- It poses more creatures per frame, not fewer, so whatever it skips by size, it re-poses more often elsewhere. The "moved on" count drops slightly (mountains 40-46 against 41-52).

### Void, being rerun

- **Swells (beach clear and storm):** the Beach place's camera looked down at a sand slope with no sea in frame, so the swells numbers don't measure the water. They're rerunning with `-benchview place,8` (the place's own yaw, out to sea).
- **Lake swells and glitter:** frozen runs (see the freeze addendum). Rerunning on m2 builds.
- **Lava and clumps:** the place names weren't found (fixed in the runner). Rerunning.

## 2026-11-08 Freeze addendum: not just the Summit; Job 33 lava rerun (q61.sh)

- **The freeze also hits job 29's lake spot** (-1624,-488) in the bench route. It isn't specific to the Summit or to main.
  - Lake: main 2 of 2 runs, water/swells 098ddff 2 of 2 (one past the 900 s timeout), water/glitter-grid 17c1b43 2 of 2 (a 447 s frame inside the measured part, and one timeout).
  - The same build with `-noswells` did not freeze in 2 runs.
  - The beach runs, and the pose shot-list sessions (`-shotlist`, not the route), did not freeze.
  - It fits the player lane's note: something in the bench route plus a jump. Job 29 and 30 builds (a21cb739, 346cf4d9) ran this lake spot cleanly, so the trigger arrived on main since then.
- **Freeze hunt next:** `q60.sh freeze` takes a Profiler capture from just before the jump (`-profilestop place <raw> -profilearrive`), and `SysMs.Worst` lists the longest frame's main-thread samples. That names the system or script.
- **Job 33 lava:** every run was skipped ("couldn't get there"). The runner only searched `DebugControls.locations`, and `VolcanoCone:centre` lives in `centres`. Fixed in the runner; rerunning on fresh m2 builds.
- **Job 33 still usable:** swells at the beach (clear and storm), and the pose shot lists. Glitter and lake swells get rerun after the freeze is found.
- **Laptop order:** clumps (running), Job 31 shots, blue moon, Job 31 RenderDoc passes, freeze hunt, lava, Job 32 rerun.

## 2026-11-08 Job 32 interim: main 46012623 freezes at the Summit place; Job 32 rerunning (q60.sh)

- **Main freezes at the Summit.** In 7 of 13 sessions at `-benchplace Summit` (-2446,1154), main 46012623 froze on the first frame after the jump, as streaming finished (frame ~110, about 33 s in).
  - The freeze lasted 5 min 45 s (04:22:48 to 04:28:33 in `q-q60-sky-plain-cs100-1.log`), 11 min in a RenderDoc session, and 13+ min (past the 900 s timeout) in one Clear session.
  - It hit all 4 runs without forced weather (plain, `-benchnoshader Sky`), and 3 of 8 with `-benchweather` forced.
  - No exception was logged and the game didn't crash. It carries on after the freeze. The last lines before the freeze are colossi placement (MagmaSerpent made solid), `Weather: forced`, and a JobTempAlloc leak warning.
  - **For whoever owns the Summit / weather / colossi code:** this looks like a long main-thread loop. Repro: a Profile build of 46012623, `-benchroute -benchstops place -benchplace Summit -benchview place,-10 -gfx High -fpscap 0 -weatherseed 4 -time 12`.
- **What was lost:** the plain and no-sky runs measured 1 frame each (the freeze used up the stop). Clear round 1 timed out. The 3 RenderDoc captures froze before they were taken, so part b never ran.
- **What's usable now:** Clear (round 2), Overcast and Storm (2 rounds each). GPU at the Summit, looking up: Clear 22.5, Overcast 23.8 / 24.1, Storm 26.3 / 26.0 ms. Overcast and Storm also have 15-35 slow frames and a 0.5 s worst frame. There's no baseline without the sky yet, so no sky cost.
- **Runner fix (b/bench-stress, uncommitted until a build compiles it):** while settling, a stop's clock now counts at most 0.25 s a frame, so a freeze no longer uses up the measured part. Any frame over 5 s is logged ("BenchRoute: <stop>: a N s frame ..."). `lib.sh` takes `RUNTIMEOUT` (default 900); the Summit sessions get 1800.
- **RenderDoc fix:** qrenderdoc's `--python` never ran a script on this laptop because RenderDoc's first-run prompts (analytics, tips) block it, and the earlier passes run hung for an hour. Set in qrenderdoc's `UI.config`: analytics opted out, tips marked seen, update checks off. `rdpasses.py` now always exits and writes any error to its output. The 7 Job 31 captures (town x3, forest x3, under, shore) are fine and get analysed next (`q59r.sh passes`).
- **Order on the laptop:** Job 33 (swells, glitter, lava, pose, clumps), then Job 31 street shots, Job 33 blue moon, Job 31 RenderDoc passes, and the Job 32 rerun (a, ard, b).

## 2026-11-08 Job 30 (refactor 3.4): water layer skip A/B, and the underwater frame without water (q58.sh)

- **Builds:** A = refactor/water-costs a21cb739 (the job 29 build), B = 346cf4d9 (skips zero-weight layers). Profile builds.
- **Runs:** 1920x1080 set explicitly, render scale 1, `-gfx High`, `-weatherseed 4`, uncapped, a fresh game per run, A B B A. 24 sessions, 0 crashes, 0 exceptions.
- **Spots:**
  - Job 29's lake spot (-1624,-488), from the shore and under the water.
  - The route's river stop (-168,392), and `-benchplace Beach` for the sea (400,300, facing out to sea).

### A and B: old against new (GPU ms, Profile timer; rounds 1 / 2)

| Spot | A (a21cb739) | B (346cf4d9) | B − A | Frame median A → B |
|---|---|---|---|---|
| Lake, shore | 28.75 (28.6 / 28.9) | 26.7 (26.9 / 26.5) | **−2.05** | 27.85 → 25.75 |
| Lake, under | 39.35 (39.9 / 38.8) | 35.4 (35.7 / 35.1) | **−3.95** | 40.15 → 35.35 |
| Sea (Beach) | 11.05 | 10.95 | −0.1 (noise) | 13.4 → 13.15 |
| River | 14.7 | 14.75 | 0 | 18.5 → 18.35 |

- **The layer skip is a real win at the lake:** −2.05 ms GPU from the shore (about 45% of job 29's ~4.5 ms "rest of the water") and −3.95 ms under.
  - The rounds don't overlap.
  - Main thread (13.6-15.1 ms) and render thread (3.7-5.5 ms) don't change.
- **River:** no change, as expected.
- **Sea:** no measurable change from the Beach view. Water covers less of the screen there, and the frame is 11 ms GPU.

### C. The underwater view without water (B only; rounds 1 / 2)

| Variant (all with water off) | GPU ms | vs water off |
|---|---|---|
| water off | 28.95 (29.0 / 28.9) | |
| + terrain off (`-benchnoshader Water,Terrain`) | 28.55 | −0.4 |
| + flora off (`Water,Flora`) | 27.55 | −1.4 |
| + terrain, flora and grass off | 26.9 | −2.05 |
| 12 m east of the pillar, plain | 39.6 (39.3 / 39.9) | (plain at the pillar: 35.4) |
| 12 m east of the pillar, water off | 32.4 (33.1 / 31.7) | |

- **Terrain, flora and grass together are only ~2 ms of the 29 ms.**
- **The pillar isn't the cost:** away from it the frame is higher, not lower.
- **What's left at 27 ms:**
  - With water, terrain, flora and grass off, the screen is almost all sky and clouds seen through the underwater fog, plus the pillar and the player (shot `q58-shots/c-wall.png`).
  - So the most likely cost is the sky shader (clouds) and the full-screen passes after it. Job 29 measured fog and tint as ~0 ms.
  - Not yet proven: `q58.sh sky` (sky off, post off, shadows off, all with water off) is queued after job 31's RenderDoc captures. The job 31 under-water RenderDoc capture also splits this frame by pass.
- Shots: `results/q58-shots`. Logs: `results/q58logs` on the laptop.
- Cost: this session so far (jobs 28-30): 264 calls, peak 260k context, $19.4 (usage_log with ~/.claude-pro-2 added).

## 2026-11-08 Job 28 (refactor 3.5): static flora for bigger cities (q56.sh, q56b.sh, q56frames.py; b/bench-stress 5c97ef08 for review)

- **Build:** Profile build of refactor/flora-static 5fa346f3 (draws grouped by material; `-nofloradetail`). The build log says `563 of 1088 variants never move, 694018 LOD0 vertices static vs 1622264 moving`.
- **Runs:**
  - 1920x1080 set explicitly, render scale 1, uncapped, `-weatherseed 4`.
  - A/B (part A, B): bigtown + Tors in one game, order static, animated (`-nostaticflora`), no detail (`-nofloradetail`), no detail, animated, static; then static and animated once each with `-benchnoshadows`.
  - C/D: a fresh game per run, order static, animated, animated, static.
  - Then one Profiler capture per variant and stop (q56b), for SetPass and draws: the Profile build's render counters read invalid in the route.
  - 20 sessions, 0 crashes, 0 exceptions.
- **New on the runner (b/bench-stress 5c97ef08):**
  - Stops `townjump`, `townenter` and `towncross` in the biggest settlement (2972,-885, 0 water on the way in along 0.88,-0.47).
  - `-benchframes <folder>`: every measured frame to a CSV, with whether the frame drew a far shadow cache slice (FloraSystem's private `cacheStep`/`CacheRedraws`, by reflection).
  - FrameTiming lags 3 frames here. Its stamps run on another clock base, so q56frames.py matches them to frames by lining up frame lengths (correlation 0.56-0.70 in all 4 runs).

### A. Grouped draws: static vs animated

| Scene | Variant | GPU ms (rounds) | Render thread ms | Main ms | Shadow pass ms | SetPass (capture) | Instanced draws (capture) |
|---|---|---|---|---|---|---|---|
| Big settlement | static | 16.45 (16.3 / 16.6) | 5.35 | 9.25 | 1.15 | 381 | 9,043 |
| Big settlement | animated | 16.75 (16.8 / 16.7) | 5.55 | 9.4 | 1.35 | 365 | 8,393 |
| Tors | static | 14.05 (14.3 / 13.8) | 3.35 | 9.4 | not measurable | 243 | 9,310 |
| Tors | animated | 14.25 (14.2 / 14.3) | 3.4 | 9.35 | not measurable | 231 | 8,184 |

- **Against job 26:** static now reads 0.2-0.3 ms less GPU than animated (job 26: −0.2 noise at the town, −0.1 at the Tors, at 1440p pixels).
  - The town's render thread is 0.2 ms lower with static. That is the only gain larger than its round-to-round spread, and it's small.
  - The GPU gap is the same size as the spread between rounds (up to 0.5 ms at the Tors), so I'd still call it within noise.
- **The split still costs passes:** with the static variant on, SetPass is +16 at the town and +12 at the Tors, and instanced draws are +650 / +1,126. One capture each, so these are indicative.
- **Shadow pass:** at the Tors the no-shadow runs read higher GPU than with shadows (14.3 / 14.6 vs 14.05 / 14.25), so the pass can't be split there on this laptop.

### B. Bake ceiling (`-nofloradetail`)

| Scene | Static | No detail | Saved |
|---|---|---|---|
| Big settlement | 16.45 | 16.25 (16.3 / 16.2) | 0.2 |
| Tors | 14.05 | 13.75 (13.5 / 14.0) | 0.3 |

- Baking the procedural weathering, wood, cloth and strata detail could save at most 0.2-0.3 ms GPU here, even at the rock-heavy Tors.

### C. First approach

- **townjump** (800 m out, jumped into the middle 15 s before the end):

  | Run | Frames over 50 ms after the jump | Worst | Frames over 33 ms in the 5 s after |
  |---|---|---|---|
  | static 1 | 53, 63, 55 (2.6-3.3 s after) | 63 | 10 |
  | animated 1 | 68, 66 (2.6-2.8 s after) | 68 | 9 |
  | animated 2 | 62 (2.7 s after) | 62 | 11 |
  | static 2 | none | 50 | 10 |

  - There is no first-use stall from the static variant: both variants show the same ~10 slow frames 2.5-3.5 s after arriving, so the cost is streaming the town in, not a shader warm-up.
- **townenter** (walked in at 6 m/s from 400 m out):
  - 3 of 4 runs had no frame over 37 ms.
  - Static run 1 had one 371 ms frame at 2807,-797, about 190 m from the middle, 66 s into the session. The other static run didn't. Cause not found: the next step would be a capture of that walk (`-profilestop townenter`).

### D. Far shadow cache redraws while walking (towncross, 100 m at 6 m/s)

- Each run redrew the cache 10 times (every 10 m), each over 4 frames: 40 slice frames of about 1,000.
- Matched per frame, against frames within 15 of a slice that drew none:

  | Variant | Slice frames | Frame ms | Main ms | Render ms | GPU ms |
  |---|---|---|---|---|---|
  | static | 80 | 16.92 vs 16.55 | 10.15 vs 9.93 (+0.2) | 4.33 vs 4.26 | 14.94 vs 13.72 (+1.2) |
  | animated | 80 | 16.42 vs 16.41 | 10.27 vs 9.74 (+0.5) | 4.25 vs 4.27 | 14.19 vs 13.76 (+0.4) |

- A redraw costs about +0.2-0.5 ms main and +0.4-1.2 ms GPU, on 4 frames every 10 m (4% of frames while walking).
- Scrolling the cache instead of redrawing it would save under 1 ms on those frames. Neither variant had a frame over 35 ms while crossing.

- Logs: `results/q56logs`, frames: `results/q56frames`, shots: `results/q56-shots` on the laptop.
- **Public repo:** `Tools/bench/lib.sh` line 3 defaults `P` to a path with a Windows user name in it. It's already on main; it should use `$HOME/Projects/Punchy`.
- Cost: this session so far (jobs 28-29): 232 calls, peak 260k context, $17.0 (usage_log with ~/.claude-pro-2 added).

## 2026-11-07 Job 29 (refactor 3.4): where the lake's GPU time goes (q57.sh)

- **Build:** Profile build of refactor/water-costs a21cb739.
- **Runs:**
  - 1920x1080 set explicitly, render scale 1, `-gfx High`, `-weatherseed 4`, uncapped, a fresh game every run.
  - Order P W S D X X D S W P per spot (2 rounds of each variant).
  - Then once each: shore `-benchnopost` and `-benchnoshadows`, and under `-benchoff Underwater`.
  - 23 sessions, 0 crashes, 0 exceptions. Every run stood at -1624,-488.
- **Spots:** job 25's lake stop at noon.
  - Shore: the player stands in the lake beside the sea-folk pillar, with water over the lower two thirds of the screen.
  - Under (`-benchpitch -35`): the camera is under water looking up, so the water surface's underside fills the whole screen.
- **Switches:** `-benchnoshader Water` turned off 3 materials (Cave water, Water, LandmarkWaterfall). `-benchoff Underwater` disabled the camera's underwater component before the camera dipped, so no fog or tint came on.

### Results (Profile GPU timer; mean of 2 rounds unless marked)

| Spot | Variant | GPU ms | vs plain | Frame median ms | vs plain | Render thread ms |
|---|---|---|---|---|---|---|
| Shore | plain | **28.9** (28.9 / 28.9) | | 27.5 | | 5.55 |
| Shore | water off | 19.05 | **−9.85** | 20.75 | −6.75 | 5.45 |
| Shore | `-waterssr 0` | 26.2 | **−2.7** | 25.6 | −1.9 | 5.6 |
| Shore | `-waterdetail 0` | 26.0 | **−2.9** | 24.85 | −2.65 | 5.45 |
| Shore | both | 23.6 | **−5.3** | 23.05 | −4.45 | 5.55 |
| Shore | no post (1 run) | 28.3 | −0.6 | 27.2 | −0.3 | 5.5 |
| Shore | no shadows (1 run) | 27.7 | −1.2 | 27.1 | −0.4 | 4.7 |
| Under | plain | **38.8** (37.8 / 39.8) | | 39.4 | | 3.85 |
| Under | water off | 29.1 | **−9.7** | 30.9 | −8.5 | 3.65 |
| Under | `-waterssr 0` | 38.55 | −0.25 (noise) | 39.65 | | 3.85 |
| Under | `-waterdetail 0` | 34.6 | **−4.2** | 35.5 | −3.9 | 3.7 |
| Under | both | 35.05 | −3.75 | 35.4 | −4.0 | 3.85 |
| Under | underwater fog/tint off (1 run) | 39.7 | +0.9 (inside plain's spread) | 39.5 | | 4.0 |

- **Water is about a third of the lake's GPU frame:** 9.85 ms from the shore, 9.7 ms under. That is far more than the 0-2 ms at fishing village 1, because here water covers most of the screen (the shore view) or all of it (under).
- **From the shore, the two switches split it about evenly and add up:**
  - The reflection march is 2.7 ms and the fine detail (ripple reads, caustics, glitter) is 2.9 ms; together they are 5.3 ms.
  - The other ~4.5 ms is the rest of the water shader and its draws.
- **Under water, the reflection march costs nothing measurable** (−0.25 ms), and the fine detail is the whole of what the switches save (4.2 ms).
  - The underwater fog and tint cost nothing measurable either.
  - So under water: ~4 ms detail, ~5.5 ms the rest of the water, ~0 reflections, ~0 fog.
- **The main thread is ~14-15 ms in every variant**, so the GPU is what limits this spot. The render thread barely moves (0.1-0.2 ms) with water off.
- **What else fills the frame:**
  - With water off, the shore view is still 19 ms GPU and the underwater view 29 ms. Post is 0.6 ms and shadows 1.2 ms of the shore frame.
  - The rest (opaque terrain, flora, the pillar) isn't split here: the GPU recorders read nothing on D3D11. The underwater 29 ms with no water drawn is high for what is on screen, and is worth a follow-up (`-benchnoshader` on the terrain and flora shaders, or the same view a few metres from the pillar) if the supervisor wants it.
- Shots: `results/q57-shots` (shore-plain, under-plain, under-nofog). Logs: `results/q57logs` on the laptop.
- Cost: this session so far (jobs 28-29, watch-queue.sh): 227 calls, peak 260k context, $16.7. `tools/usage_log.py` reads only `~/.claude/projects`; this laptop session runs from a second Claude config folder (`~/.claude-pro-2`), so the CSV in this commit was made with that folder added (49 rows, $633). Jobs 24-27 had missed it.

## 2026-11-07 Job 27 (refactor 3.2): fire instance buffer A/B (q55.sh, q55b.sh; b/bench-stress 87412ee1 for review)

- **Builds:**
  - A = main 01cb543 (e5c88fb4 game code).
  - B = refactor/fire-budgets 96573009 (fire-instances 03783ca9 plus the Ember Forest near/far hand-off fix).
  - Both are Profile builds.
- **Runs:**
  - 1920x1080 set explicitly, render scale 1, uncapped.
  - A B B A per scene, then each build once with `-benchoff LandmarkEffectSystem`.
  - Then one Profiler capture per build and scene (`-profilestop`), read by SysMs.
  - 16 sessions, 0 crashes, 0 exceptions.
- **Scenes:**
  - Ember Forest burning at night: `-benchplace EmberForest: -benchhour 22 -benchview 120,5`.
  - Job 25's 500-fire scene (`-benchfires 500`) and the same spot without flames (`firesbase`), at noon.
- **New on the runner:**
  - A flame counter: flames handed to FireSystem each frame, read just before LandmarkEffectSystem draws them.
  - `FireSystem.Dropped` where the build has it.
  - `-benchview yaw,pitch` at the place stop.

### Results (means of the A B B A rounds; LandmarkEffectSystem from the captures)

| Scene | Build | Frame median ms | Main ms | GPU ms | LandmarkEffectSystem.Update main ms | Flames a frame | Dropped |
|---|---|---|---|---|---|---|---|
| Ember Forest, night | A main | 15.1 | 12.6 | 10.5 | **4.23** | 3152 | n/a (no counter on main) |
| Ember Forest, night | B branch | 14.1 | 9.85 | 12.1 | **1.54** | 3152 | 0 |
| 500 fires | A main | 16.65 | 10.75 | 15.3 | **1.36** | 500 | n/a |
| 500 fires | B branch | 16.1 | 10.0 | 15.1 | **0.19** | 500 | 0 |
| No flames (firesbase) | A / B | 15.45 / 15.5 | 9.2 / 9.4 | 15.15 / 14.9 | | | |

- **Main thread:** the branch cuts LandmarkEffectSystem by 2.7 ms in the Ember Forest (−64%) and by 1.2 ms with 500 fires (−86%).
  - At the Ember Forest that takes the frame from main-bound (main 12.6 ms against GPU 10.5) to GPU-bound.
  - Frame median: −1.0 ms in the Ember Forest, −0.55 ms with 500 fires.
- **Flame count:** the same in both builds (3152 Ember Forest, 500 benchfires), and B drops none.
- **GPU for the flame passes: not measurable cleanly on this laptop.**
  - With 500 fires, flames add only +0.15 ms (A) and +0.2 ms (B) of GPU over the same spot without them, which is within noise.
  - In the Ember Forest, A reads less GPU than B (10.5 vs 12.1). But A with LandmarkEffectSystem off reads more (12.0) than A with it on, which flames cannot cause.
  - A is main-bound there, and the laptop GPU most likely clocks down while it waits. So the Ember Forest GPU numbers are not comparable between A and B, and I'd not read the +1.6 as a flame cost.
  - The off runs are only a GPU baseline. With LandmarkEffectSystem off nothing clears the flame list, so it sits at the 8192 cap and B counts drops; those flames are never drawn.
- **Draw counts (captures):**

  | Scene | Build | SetPass calls | Instanced draws | Tris |
  |---|---|---|---|---|
  | Ember Forest | A | 136 | 3.7k | 1.27M |
  | Ember Forest | B | 138 | 3.1k | 1.33M |
  | 500 fires | A | 293 | 2.6k | 1.56M |
  | 500 fires | B | 294 | 3.4k | 1.78M |

- **Looks:** the Ember Forest screenshots from A and B look the same by eye at yaw 120, pitch 5 (near and far flames, the same glow). The 500-fire shots also match. This is a by-eye check, not a pixel comparison.
- Logs: `results/q55logs` on the laptop. Shots: `results/q55-shots-a`, `results/q55-shots-b`.

## 2026-11-07 Job 26 (refactor 3.5b): static flora variant A/B on refactor/flora-static 5febffdb (q54.sh; b/bench-stress 7aa25cf0 for review)

- **Build:** Profile build of 5febffdb. The build log says `[flora] static (refactor 3.5): 563 of 1088 variants never move, 694018 LOD0 vertices static vs 1622264 moving`.
- **Runs:**
  - The window is 1920x1080 at render scale 1.333 (1440p pixels).
  - The window size is set explicitly every run, and every shot came out 1920x1080.
  - Uncapped, in order A B B A, where A = static variant and B = `-nostaticflora`.
  - Then one A and one B with `-benchnoshadows`, so the shadow pass = GPU minus GPU without shadows.
  - 12 sessions, 0 crashes, 0 exceptions.
- **Scenes:** the job 25 big settlement, the dense forest (under the OakGrove canopy), and the Tors.
  - The Tors used new route stop `-benchplace Tors`, at 2081,2445. Its first run left it out of `-benchstops`, so it was rerun alone with the same order.

### Results (GPU ms, Profile timer; rounds 1 / 2)

| Scene | Static (A) | Animated (B) | A − B | Shadow pass, static / animated | Main ms A / B |
|---|---|---|---|---|---|
| Big settlement | 24.1 / 24.4 | 24.5 / 24.4 | −0.2 (noise) | 0.4 / 0.6 | 9.2 / 9.1 |
| Dense forest | 16.4 / 16.1 | 16.2 / 16.0 | +0.15 (noise) | 0.8 / 0.7 | 9.7 / 9.7 |
| Tors | 22.2 / 22.2 | 22.3 / 22.3 | −0.1 | 1.8 / 1.6 | 8.9 / 9.1 |

- **The static variant makes no measurable difference** at the settlement or the forest. Their round-to-round spread (0.2-0.3 ms) is bigger than the A−B gap.
  - The Tors shows a steady −0.1 ms, which is about 0.5% of its GPU time.
  - So vertex wind is not what limits these scenes.
- **What the flora in view is** (`-benchflora`, plants vs rocks/kit):

  | Scene | Plants in view | Rocks/kit in view |
  |---|---|---|
  | Big settlement | 4,522 (927k tris) | 1,156 (227k tris) |
  | Forest | 10,932 (2.27M tris) | 1,008 (139k tris) |
  | Tors | 7,152 (1.21M tris) | 894 (321k tris) |

  - Even at the Tors only about 21% of flora tris in view are rocks/kit. So the static variant touches a fifth of the vertices at best, and the wind maths it removes is cheap.
- **Main opaque pass:** cannot be split on this build. The GPU recorders read nothing on D3D11, so only the shadow pass is split, by subtraction.
- **Looks:** I compared only the A screenshots by eye (big settlement, forest, Tors). They look normal. There are no B shots, so this is not a pixel comparison; the player lane's same-scene check stands.
- Logs: `results/q54logs` on the laptop. Shots: `results/q54-shots`, `results/q54t-shots`.

## 2026-11-07 Job 25 (refactor 1.2): stress scenes on e5c88fb4, uncapped (q53.sh; b/bench-stress 472b629d for review)

- **Build:** Profile build of 01cb543, the same game code as job 24.
- **Runs:**
  - The window is 1920x1080, at render scale 1 (1080p) and 1.333 (1440p pixel count; the laptop desktop clamps bigger windows).
  - Order ABBA over 2 rounds.
  - 12 timed sessions plus 5 capture sessions, read by SysMs at scale 1.
  - 0 crashes, 0 exceptions.
- **New runner switches** (on b/bench-stress, for review):
  - `-benchpitch <deg>`: the camera's pitch on arrival. It is needed for the underwater view at render scale, because only the route applies render scale and it resets the pitch to 12.
  - `-benchflora`: reads each flora layer's cull counts back from the GPU and logs instances and tris in view, split into plants and rocks/kit buildings (FloraForm.Bones onward).
- **How to read the numbers:**
  - Frame = median. GPU is the Profile timer. Main and render thread are means from the runner.
  - Draw counts, tris and top 5 come from the scale-1 capture. Draw Calls and Batches are still not recorded, so the table shows SetPass calls and instanced batched draws.

### Results (1080p | 1440p pixels)

| Scene | Frame ms | GPU ms | Main ms | Render thr ms | Worst ms | Bound | SetPass / instanced draws / shadow casters | Tris |
|---|---|---|---|---|---|---|---|---|
| Big settlement (46 buildings of 152 on 6 settlements, at 2972,-885) | 19.1 \| 24.6 | 16.9 \| 24.5 | 9.3 \| 9.2 | 5.5 \| 5.2 | 30 \| 29 | GPU \| GPU | 361 / 10.8k / 556 | 3.1M |
| Dense forest (under the OakGrove canopy, 1740,3355) | 14.6 \| 18.3 | 12.9 \| 16.6 | 9.1 \| 9.8 | 4.5 \| 4.3 | 26 \| 27 | GPU \| GPU | 344 / 8.9k / 239 | 3.5M |
| Fires base (start spot, no flames) | 15.7 \| 24.4 | 14.7 \| 24.3 | 9.6 \| 9.2 | 3.6 \| 3.4 | 41 \| 40 | GPU \| GPU | not captured | |
| 500 fires (same spot, `-benchfires 500`) | 16.9 \| 26.4 | 15.7 \| 25.8 | 11.1 \| 10.6 | 3.8 \| 3.5 | 40 \| 34 | GPU \| GPU | 293 / 3.2k / 219 | 1.7M |
| Lake at noon (-1624,-488, yaw 0) | 25.2 \| 44.8 | 25.1 \| 45.8 | 10.6 \| 11.9 | 4.1 \| 4.2 | 43 \| 66 | GPU \| GPU | 249 / 4.2k / 336 | 2.0M |
| Lake, camera under water (`-benchpitch -35`) | 38.2 \| 66.5 | 37.8 \| 64.3 | 10.9 \| 16.6 | 2.9 \| 3.5 | 50 \| 83 | GPU \| GPU | 151 / 3.0k / 294 | 1.2M |

### Top 5 systems (SysMs, scale 1; ms per frame, all threads / main)

| Scene | Top 5 |
|---|---|
| Big settlement | shadows (URP) 2.62/0.07, creature render 1.93/0.30, creature brain 1.49/0.06, flora 0.84/0.84, colossi 0.38/0.38 |
| Dense forest | creature render 2.57/0.51, creature brain 1.86/0.07, shadows (URP) 1.24/0.07, flora 0.61/0.61, colossi 0.33/0.33 |
| 500 fires | LandmarkEffectSystem.Update (FireSystem.Draw) 1.31 main, creature brain 1.37/0.06, shadows (URP) 1.18/0.08, creature render 1.12/0.34, flora 0.69/0.69 |
| Lake | creature render 1.73/0.28, shadows (URP) 1.60/0.07, creature brain 1.36/0.06, flora 0.72/0.72, fauna director 0.70/0.70 |
| Under water | shadows (URP) 1.35/0.06, creature brain 1.28/0.05, fauna director 1.07/1.07, creature render 0.82/0.25, flora 0.75/0.75 |

### Flora in view (`-benchflora`, end of each stop)

| Scene | Plants in view | Rocks/kit in view | Plant share of in-view flora tris |
|---|---|---|---|
| Dense forest | 10,932 (2.27M tris) | 1,008 (139k tris) | 94% |
| Big settlement | 4,522 (927k tris) | 1,156 (227k tris) | 80% |
| Fires spot | 2,959 (642k tris) | 744 (86k tris) | 88% |
| Lake (the far shore) | 7,936 (1.33M tris) | 1,000 (174k tris) | 88% |
| Under water | 3,861 (539k tris) | 257 (53k tris) | 91% |

- **Shadow lists:** the flora shadow lists read nearly empty (0-121 instances).
  - Most flora shadows are probably drawn from FloraSystem.ShadowCache, which keeps its own counts that this switch does not read.
  - So take the in-view numbers as the main view only.

### Notes

- **Every scene is GPU-bound at both sizes.** The main thread stays at 9-12 ms everywhere, except under water at 1440p (16.6).
- **The lake is the worst scene by a wide margin:**
  - Noon: 25 ms GPU at 1080p, rising to 46 ms at 1440p pixels (+83%).
  - Under water: 38 ms, rising to 64 ms (+70%).
  - Both are pixel-bound, consistent with q43 (water is GPU and fill).
- **Pixel scaling elsewhere:**

  | Scene | GPU from 1080p to 1440p pixels |
  |---|---|
  | Fires spot | +65% |
  | Big settlement | +45% |
  | Dense forest | +29% (vertex-bound, as in q51) |

- **500 fires**, laid in a 4 m grid by `-benchfires 500` (FireSystem.AddFlame every frame):
  - +1.0 ms GPU at 1080p and +1.5 ms at 1440p.
  - +1.5 ms main thread, nearly all of it LandmarkEffectSystem.Update (1.31 ms, which includes the per-frame mesh rebuild that refactor 3.2 removes).
  - The Ember Forest at night is left to job 27, whose branch counts flames.
- **The big settlement has the most URP shadow work:** 556 shadow casters and 2.6 ms of shadow CPU across all threads.
  - The 46 kit buildings are 1,156 rocks/kit instances (227k tris) against 4,522 plants (927k tris) in view.
  - So flora vertex work there is mostly plants. Job 26 will measure the static variant directly.
- **The lake spot puts the player inside a sea-folk pillar standing in the water.** It stays mid-frame at yaw 0 and 180, and the water fills about two-thirds of the screen.
  - Yaw 180 measured the same GPU (24.7 ms in the dry run), so I kept the card's spot.
- **The 1440p lake runs have many slow frames** (215-379 over 33 ms per stop), which follows from their 45-66 ms medians.
- Logs: `results/q53logs` on the laptop. Shots of each stop: `results/q53-dry-*`.

## 2026-11-07 Job 24 (refactor 1.1): population sweep on e5c88fb4, uncapped (q52.sh)

- Build: Profile from main 01cb543. That is e5c88fb4 plus PR #197 only, which touches bench scripts and no game code, so it is the baseline.
- Settings: `-place meadow -time 12 -gfx Low -fpscap 0`, 1920x1080, `-wildcap N`.
  - The population fills for 70 s, then 7 samples of 3 s each, every one with `-perflog`.
  - 2 rounds, the second in reverse order.
  - 24 sessions, 0 exceptions, 0 crashes.
- Profiler captures: one SysMs capture per N (2000 frames) in mode `def`, round 1.
  - The capture adds 1.2-2.4 ms of main thread. So the `def` rows below use round 2 only, and the SysMs numbers are for shares and per-system ms only.
- `creatures` counts every live CreatureState, not just wild creatures. At cap 180 it reads about 265.
  - Cap 2000 levels off at about 1350 live after 70 s.
- Modes:
  - `near` = `-nobrainlod`: every creature thinks every frame.
  - `def` = the default LOD (80 m, every 4th frame beyond it). This already skips about half the creatures at the meadow.
  - `far` = `-brainlod 20,4`: only creatures within 20 m think every frame. This is "mostly far" and needs no new flag.

### Per N (default LOD; mean of 7 x 3 s samples; worst = max over samples)

| N cap | live | frame ms (worst) | main | render thr | GPU | brain wall (worst) | worker spread | brain summed | brain wait | pose median (worst) | posed/frame | advance | audio | trace | mantrap | wake |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 180 | 264 | 12.3 (34) | 11.0 | 2.99 | 7.0 | 0.77 (3) | 1.31x | 3.9 | 0.063 | 1.72 (3) | 27 | 1.0 | 0.19 | 0.12 | 0.03 | 0.03 |
| 500 | 431 | 12.6 (33) | 11.3 | 2.95 | 7.2 | 1.05 (5) | 1.17x | 6.0 | 0.073 | 3.24 (9) | 53 | 1.7 | 0.20 | 0.10 | 0.04 | 0.06 |
| 1000 | 718 | 12.8 (35) | 11.5 | 2.89 | 7.4 | 1.82 (8) | 1.11x | 11.1 | 0.075 | 3.67 (15) | 76 | 3.1 | 0.22 | 0.06 | 0.06 | 0.09 |
| 2000 | 1330 | 13.1 (35) | 12.3 | 2.94 | 7.9 | 2.88 (9) | 1.05x | 18.5 | 0.115 | 7.94 (15) | 161 | 5.0 | 0.27 | 0.29 | 0.09 | 0.14 |

### SysMs per N (def, round-1 captures; ms per frame, all threads / main thread)

| N cap | capture frame ms | creature brain | creature render | fauna director | creature audio | PresentationSystemGroup (main) | SimulationSystemGroup (main) | instanced draws | tris | GC KB/frame (allocs) | total used / gfx / GC heap MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 180 | 13.64 | 4.00 / 0.071 | 2.35 / 0.34 | 0.40 | 0.20 | 1.17 | 1.13 | 7.2k | 2.0M | 2.7 (42) | 1293 / 558 / 38 |
| 500 | 13.93 | 7.05 / 0.076 | 3.28 / 0.37 | 0.45 | 0.22 | 1.31 | 1.18 | 10.3k | 2.2M | 4.9 (43) | 1291 / 558 / 34 |
| 1000 | 13.75 | 11.26 / 0.086 | 5.55 / 0.48 | 0.45 | 0.23 | 1.60 | 1.18 | 22.5k | 3.5M | 2.1 (41) | 1296 / 558 / 39 |
| 2000 | 15.34 | 20.83 / 0.129 | 9.58 / 0.67 | 0.59 | 0.34 | 2.99 | 1.45 | 36.9k | 5.6M | 2.5 (51) | 1299 / 558 / 40 |

- SetPass calls stay at 159-166 and shadow casters at about 203 at every N. Draw Calls and Batches counters are still not recorded.
- `-gclog` printed nothing on this build, so the GC numbers come from SysMs. They show no trend with N.
- Native memory: Total Used rises 6 MB from 180 to 2000, and Gfx stays at 558 MB. System Used reads 1.89-2.05 GB with no trend.
- GPU ms for creatures alone: SysMs has no per-system GPU split. The whole-frame GPU rises 7.0 to 7.9 ms (def) and 7.1 to 9.1 ms (near) as tris go from 2.0M to 5.6M. q45 put the creature GPU at about 0.15 ms at 265 creatures.

### Near / default / far split (main ms, brain wall, brain summed over workers, advance ms, brain wait)

| N cap | near (-nobrainlod) | def (80 m) | far (-brainlod 20,4) |
|---|---|---|---|
| 180 | 11.1 / 1.01 / 5.9 / 2.1 / 0.07 | 11.0 / 0.77 / 3.9 / 1.0 / 0.06 | 11.1 / 0.58 / 2.8 / 0.6 / 0.07 |
| 500 | 11.6 / 1.72 / 10.0 / 3.7 / 0.07 | 11.3 / 1.05 / 6.0 / 1.7 / 0.07 | 11.4 / 0.82 / 4.5 / 0.9 / 0.08 |
| 1000 | 11.8 / 2.48 / 15.9 / 5.7 / 0.08 | 11.5 / 1.82 / 11.1 / 3.1 / 0.08 | 11.8 / 1.28 / 7.6 / 1.5 / 0.08 |
| 2000 | 13.6 / 4.36 / 28.7 / 10.0 / **0.85** | 12.3 / 2.88 / 18.5 / 5.0 / 0.12 | 12.3 / 2.18 / 13.7 / 2.6 / 0.11 |

- Thinks per frame at 2000: near 1320, def 656, far 341.
- Pose does not change with the brain LOD: median 7.9-8.4 ms at 2000 in all three modes.

### What the supervisor asked for

- **Main-thread brain prep as a share of the frame:**

  | N cap | brain prep (main) | share of frame |
  |---|---|---|
  | 180 | 0.071 ms | 0.5% |
  | 500 | 0.076 ms | 0.5% |
  | 1000 | 0.086 ms | 0.6% |
  | 2000 | 0.129 ms | 0.8% |

  - Brain prep here is SysMs CreatureBrainSystem + BrainJob on the main thread: snapshot arrays, lookup and grid build.
  - The snapshot copy itself is a scheduled CopyJob. Its 0.000 ms on the main thread is real: CopyTicks only times the Sync and fallback paths, so the job's own time is not measured.
  - `brain wait` stays at 0.06-0.12 ms, except at 2000 near, where it reaches 0.85 ms. That is the first point where the brain job lands on the main thread's critical path.
- **Growth from cap 180 to 2000 (live 264 to 1330, x5.0):**

  | Cost | 180 → 2000 | Growth | Thread |
  |---|---|---|---|
  | Brain, summed over workers | 4.0 → 20.8 ms | x5.2 | Workers. Wall 0.77 → 2.88 ms (x3.7) |
  | Pose job median | 1.72 → 7.94 ms | x4.6 | Workers. Worst 15 ms |
  | Creature render | 2.35 → 9.58 ms | x4.1 | All threads |
  | Creature render, main thread | 0.34 → 0.67 ms | x2.0 | Main |
  | Readers (audio + trace + mantrap + wake) | 0.37 → 0.79 ms | x2.1 | Main |
  | Brain prep | 0.07 → 0.13 ms | x1.8 | Main |
  | Snapshot | ≈ 0 | — | Main |

  - Brain grows linearly with thinks, at about 14 µs of worker time per creature-think.
  - Pose grows with creatures posed per frame (27 → 161).
  - Trace is noisy (0.06-0.47 ms between samples).
  - Brain prep and the snapshot are the cheapest items.
- **On the main thread:**
  - The frame grows 1.3 ms (def) to 2.5 ms (near) from 180 to 2000.
  - The largest main-thread grower is PresentationSystemGroup, +1.8 ms in the captures. It holds creature render's main part (+0.33) and the pose/render job waits.
  - Next come the readers (+0.4), fauna director (+0.19) and brain prep (+0.06).
- **Bound:** CPU-bound (main thread) at every N. Main is 11-13.6 ms against GPU 7-9 ms.
  - The creature pipeline is a small part of that main thread: the meadow frame's main thread is mostly other systems (colossi 1.0, flora 0.6, weather, caves).
- **Suggested order (from the numbers only):**
  1. Pose and render (2.3-2.5): grows almost as fast as brain, and is the part that reaches the main thread.
  2. Brain worker time: the largest absolute number, but off the main thread until about 1300 near.
  3. Readers.
  4. Brain prep and snapshot: under 1% of the frame at every N.
- Logs: `results/q52logs` on the laptop (not pushed). Script: `lanes/profiler/scripts/q52.sh`.

## 2026-11-07 refactor 1.2: stress stops on v0.13.0 (4fea789), uncapped (b/bench-stress dd1d4738 for review; q51.sh, q51b.sh)

- DONE: new runner stops, used only when named in `-benchstops`:
  - `bigtown`: the settlement with the most buildings.
  - `firesbase` / `fires`: the same spot without and with `-benchfires N` flames (default 500).
  - Also measured: `forest`, `lake`, and an under-canopy spot (`-benchspot 1740,3355,180`; `forest` only looks at OakGrove from a meadow).
  - `lib.sh` run() takes BENCHW/BENCHH for the window size.
- Builds: Bench and Profile, 2 rounds each in reversed order. 0 crashes, 0 exceptions.
- Gotcha: the laptop's logical desktop is 1920x1080, so 2560x1440 windows silently stayed at 1080p (checked with a screenshot). q51's "2560" rows are extra 1080p rounds. The 1440p pixel count was measured as 1080p at render scale 1.333 (post and UI excepted, q51b, Profile, A B B A).

Means. Frame and GPU ms are from Profile builds (Bench under-reads GPU). Main thread ms is from the 4 Bench sessions.
| Stop | GPU 1080p | GPU 1440p pixels | Main thread (Bench) | Render thread | avg fps 1080p (Bench) |
|---|---|---|---|---|---|
| lake | 10.6 | 17.9 (+69%) | 8.6 | 2.2 | 72.9 |
| forest (edge) | 13.7 | 19.8 (+45%) | 8.2 | 2.7 | 67.9 |
| spot (under canopy) | 14.4 | 17.4 (+21%) | 15.1 | 7.7 | 56.5 |
| bigtown (46 buildings) | 17.3 | 25.3 (+46%) | 8.6 | 5.0 | 56.7 |
| firesbase | 14.8 | 24.6 | 9.1 | 3.4 | 61.8 |
| fires (500) | 16.1 | 25.4 | 14.1 | 5.1 | 52.6 |

- CPU or GPU bound:
  - At 1080p on the GTX 1060 Max-Q, every calm stop is GPU-bound: GPU 10.6-17.3 ms vs main thread 8-9 ms.
  - At 1440p pixels the GPU rises 45-69%, so it is pixel-bound. The exception is under the canopy (+21%): flora there is vertex/draw bound, matching q44.
  - `fires` and `spot` are the CPU-limited cases: main thread at or above the GPU frame.
- Fires:
  - 500 flames add about 5 ms main thread and 1.7 ms render thread on Bench (9.1 -> 14.1 / 3.4 -> 5.1). GPU adds only about 1.3 ms at 1080p.
  - The cost is the per-frame mesh rebuild and upload, as roadmap 3.2 expects.
  - Spread is wide: avg fps per session 47-58, and the first check run measured only +1.2 ms main thread. Treat 5 ms as an upper-middle figure.
- bigtown:
  - The biggest settlement on v3 has **46 buildings** (152 across all 6 settlements), so the roadmap's "100 kit buildings" scene doesn't exist.
  - It is the heaviest GPU stop at 1080p (17.3 ms), with render thread 5.0 ms.
  - A 100-building case needs a test layout (world lane) or a different island.
- spot:
  - Main thread 15 ms, render thread 7.7 ms, and many slow frames (up to 114 in a session; 1% low 20-33 fps).
  - Confound: it runs right after a 3.8 km jump from `fires`, with a 10 s settle, so streaming may be in it. The `forest` stop next to it (arriving at boot) is clean.
  - Next: run it first in its own session with a capture.
- lake: water fills about half the screen. It is the most pixel-bound stop (+69%), which matches q43 (water GPU 2.1 ms).
- Shots of each stop: results/q51-dry/*.png on the laptop (not committed).
- NOT DONE:
  - True 1440p: needs a bigger desktop or an exclusive-fullscreen run, and fullscreen on this machine is Zach's call.
  - Draw-call counts: none on these builds.
  - The 100-building settlement.
- NEXT:
  - The canopy spot in its own session with a Profile capture.
  - Phase 1.1 population sweep, once the animal lane has a spawn-count flag.
- Cost: not logged (usage_log run after this entry).

## 2026-11-07 job 23: shared scripts end on their own and touch only their own processes (b/fix-proc-cleanup 9ef5867e, for review)

- DONE: `run-playmode-tests.sh` runs Unity under `timeout 3600` and reports a timeout.
- DONE: `smoke.sh` runs the game under `timeout 400` and closes it by its own PID, found from its log path. It no longer uses `Stop-Process -Name Punchy`.
- DONE: `strays()` moved to `Tools/bench/strays.ps1` with a `-Tree` argument. It kills batch Unity and bench games of this tree, and bee_backend whose parent is gone or being killed.
- DONE: `mpsmoke` stops the client loop as soon as the host run ends. This differs from the card's `timeout 1000` wrapper because that would still leave `wait` hanging for up to 1000 s.
- Checks, using decoy processes in fake trees (old vs new):
  - `strays`, other tree's bench game and batch editor: old killed both, new keeps both.
  - `strays`, this tree's bench game and batch editor: new kills both.
  - `strays`, user's game and interactive editor: both versions keep them.
  - `strays`, bee_backend: new kills a true orphan and the child of a killed editor, and keeps the child of the other tree's editor. Old kept every bee_backend while the decoy interactive editor was alive, so this is not a like-for-like comparison.
  - `mpsmoke` with the host ending before its world is up: old still waiting at 25 s (loop allows 900 s), new returned after 8 s (stubbed 2 s host runs, 2 rounds).
  - `run-playmode-tests.sh` with a fake editor and the limit lowered to 3 s: stopped, printed "Timed out", exit 1.
  - `smoke.sh` on a real Bench build (2049f8c), 3 caps, keyboard and mouse driving stubbed out, decoy Punchy.exe running: 0 exceptions, 0 games left, decoy survived.
- NOT tested: a real hung Unity editor, a real two-game `mpsmoke`, or `smoke.sh` with live input. bee_backend with a process between it and Unity would not be caught until that process exits.
- Found, not fixed: `Tools/bench/drive.ps1` picks its target with `Get-Process Punchy | Select-Object -First 1` and sends real keystrokes and focus changes. With another Punchy.exe running it can drive the wrong game. Fix suggestion: pass a PID.
- Numbers: this is script hygiene, so there are no frame-time numbers (the perf-fix before/after rule doesn't apply).
- Learned: Git Bash `( exe & )` keeps a stub bash as the exe's parent, so it does not make an orphan in tests. Use `Start-Process`.
- NEXT: refactor Phase 1.2 stress scenes.

## 2026-11-06 q50e: after Zach's reboot (13:29) the capped 0.5 s present waits are gone

- Bench build of 2049f8c, run stop, back to back (q50e.sh): **capped at 60, 3 of 3 sessions clean** (avg 59.0-59.2 fps, worst 29.7-34.3 ms, no hitch over 100 ms); uncapped 2 of 2 clean (avg 119-120 fps, worst 22-32 ms). Before the reboot the same build stalled 494-533 ms in 6 of 6 capped sessions.
- So it was machine state on this laptop since about 08:00-09:45 that a restart cleared (Zach found nothing unusual on screen). Not code. Cause still unknown; if 0.5 s DXGI.WaitOnSwapChain frames come back in capped runs, try an uncapped run and a restart before bisecting.
- Profile captures also clean after the reboot: q50.sh on 2049f8c, run + salt x3 capped at 60: **0 frames over 100 ms in 6 captures** (before: 20 waits in 6).
- q49's four waits were this too; no player-side action unless it is seen on another machine.
- Cost: 8 calls, peak 60k context, $0.4

## 2026-11-06 q50: the 0.5 s present waits come from the 60 fps frame cap on this laptop since this morning, not from any code change

**Short answer:**
- **The animal editor is not on this laptop.** The only Unity process here is a 14 MB windowless console unity.exe, running since Nov 1. Memory is fine too: 22 of 32 GB free, paged pool 1 GB, so the paged-pool leak isn't here either. The "editor closed" comparison can't be done here, so I bisected instead.
- **The trigger is `-fpscap 60`** (FrameRateCap: vSyncCount 0, Application.targetFrameRate 60). Same Bench build of 2049f8c, run stop, back to back:
  - **capped at 60: 6 of 6 sessions have 1-3 frames of 494-533 ms**, Profile and Bench builds alike, with or without a capture;
  - **uncapped: 0 of 3** (worst 14-16 ms).
- **It is not code.** The 781fa5a Profile build made at 06:27 was clean in q46 (06:36) and once at 10:16. At 11:05 the same build had 8 waits in 6 captures. A fresh 781fa5a build, a ba97b20 build, a 2049f8c build and a 2049f8c build with the bench probe's boot removed all show them. The bench probe is cleared as well.
- **It started on this laptop between about 07:00 and 09:45 today.** Capped Profile captures were clean before that (q40; q46 06:36; q47 07:00, 4 sessions). From q49 at 09:45 on, nearly every capped capture had them.
- **Every one has the same shape:** main sits 474-498 ms in DXGI.WaitOnSwapChain (WaitForLastPresentation), the render thread idles, and game code is normal. A present that isn't retired for about 0.5 s looks like a display or compositor stall: this is an Optimus laptop (GTX 1060 Max-Q renders, Intel HD 630 drives a 3840-wide 60 Hz display).
  - Uncapped, the game never waits on the frame-latency object, so it doesn't see it.
  - The keep-awake helper holds ES_DISPLAY_REQUIRED, and the System log shows no display or GPU driver events today.
- **What changed at about 08:00-09:45 is unknown.** It's environment, not code. A display setup change (an external 4K monitor, resolution or scaling) is my first guess, and only Zach can say.
- **Effects:**
  1. **Profiler-lane method:** my Profile captures use -fpscap 60 (so 2000 frames cover a stop). Until this is cleared they'll carry false 0.5 s frames. I'll treat any 0.5 s DXGI.WaitOnSwapChain frame in a capped run as this, and capture uncapped where possible.
  2. **q49's 4 waits** were this too, not #175. q49's Bench results (uncapped) stand.
  3. **Possible player issue:** a player with the 60 cap on a laptop in the same state would see it. Worth one check on the main PC (cap 60, an Optimus or iGPU display) before calling it laptop-only.

**Method:**
- run and salt walk-up captures (q50.sh) on:
  - 2049f8c: 3 x 2, 20 waits;
  - ba97b20: 2 x 2, 5;
  - 2049f8c with no probe boot: 2 x 2, 6;
  - a fresh 781fa5a build: 1, crashed on quit (known, fixed in #175);
  - the old 781fa5a build: 3 x 2, 8.
- No-capture checks, run stop: q50c.sh (Profile + Bench at -fpscap 60) and q50d.sh (Bench uncapped).
- Logs: results/q50logs.
- Cost: 22 calls, peak 290k context, $2.6

## 2026-11-06 q49: q46 rerun in full on main 2049f8c (quit-crash fix PR #175): no crash, hitches gone; four 0.5 s present waits in Profile captures

**Short answer:**
- **No crash** in 9 quits: 2 route sessions, 4 join/leave processes and 3 Profile runs all closed cleanly. The SurfaceGlow quit crash did not return. It was 1 in about 10 quits in q46, so 9 clean quits is encouraging, not proof.
- **Hitches are gone on this main:**

  | | job 28 | q40 | q46 (781fa5a) | **q49 (2049f8c)** |
  |---|---|---|---|---|
  | join/leave host: frames over 33 ms | 2 | 60 | 3 / 2 | **0 / 0** |
  | join/leave host: 1% low fps | 55.5 | 31.1 | 63.1 / 54.3 | **63.0 / 51.6** |
  | join/leave client: frames over 33 ms | 0 | 26 | 0 / 0 | **0 / 0** |
  | townwalk: worst ms (1% low fps) | 26.2 | 331 | 418 / 29.8 | **25.1 (65.0) / 22.6 (69.1)** |
  | run: frames over 33 ms (avg fps) | 3 (100) | 24 (88) | 0 / 0 (121 / 118) | **0 / 0 (130.5 / 128.5)** |
  | glide: frames over 33 ms (avg fps) | 64 (59) | 23 (77) | 0 / 1 (98 / 87) | **0 / 2 (102 / 74)** |

  - The townwalk's saguaro skin frame (q48, A-82 row 4, not merged yet) fell outside the measured window in both sessions, as in q48. It isn't fixed yet; it just wasn't counted.
- **FaunaDirector is out of the spike frames** in all 3 Profile captures.
  - Its inclusive mean is 0.39-0.50 ms against a median of 0.07, because of its time-sliced Comfort steps, which top out at 4-7 ms (q40: 10-28 ms spikes).
  - The Comfort slices still reach 4-5 ms on some frames. If the frame budget gets tight, a smaller slice would help.
- **New: four frames of 0.5 s, all in Profile captures, all the main thread waiting on the GPU present:**
  - run: 509 and 520 ms, 8 s apart; salt walk-up: 502 and 521 ms, 25 s apart; fishing village: none.
  - In each one, main spends **474-498 ms in DXGI.WaitOnSwapChain** (inside WaitForLastPresentation). The rest of the frame is normal script and ECS work (under 10 ms each). The render thread is idle (Semaphore.WaitForSignal).
  - **So the GPU, or the driver's present, took about 0.5 s; the game's CPU code didn't.**
  - None in the 4 Bench sessions (no frame over 44 ms in run or glide). None in q40's or q47's Profile captures on older mains.
  - **First suspect: the animal lane's Unity editor**, open on this laptop for this run (the supervisor's note). It shares the one GPU, and an editor repaint, shader compile or bake can hold it for that long. W-114, W-115 and W-116 in #175 are the other new factor.
  - To tell them apart: rerun the run + salt captures with the editor closed. If the waits stay, it's in #175. About 25 minutes whenever the editor is shut.

**Method:**
- q46.sh retargeted (q49.sh): Bench + Profile builds of 2049f8c, each started only with 6 GB or more free (21 GB was free both times).
- Bench, uncapped, frames over 33 ms: v2 townwalk, run, glide, and the host + client join/leave at the v2 cave, 2 sessions each.
- Profile captures (-fpscap 60, last 2000 frames) of run, the v2 fishing village and the salt walk-up, through BigFrame (call trees of the 0.5 s frames) and BProfileTools.
- Logs: results/q49logs.
- Cost: 10 calls, peak 275k context, $1.2

## 2026-11-06 q48: Millfield big frame traced (most likely) to the WalkingSaguaro colossus skinning on the main thread, main 781fa5a

**Short answer:**
- **Every session has one 459-465 ms frame in MonoBehaviour Update** (Update.ScriptRunBehaviourUpdate 436-441 ms). It comes **right after "ColossusRig: saguaro skinned (178950 verts...)"** in all 4 probe sessions.
  - That's the WalkingSaguaro colossus (2925,-1710) building its skin weights in managed loops over 179k vertices: Fauna/ColossusRigs.cs, the saguaro builder, about lines 200-290.
  - It runs **once, when the camera first comes in range**, which on the v2 townwalk happens near its start in east Millfield.
- **That fits the Millfield frame:**
  1. Same size (331 / 418 ms then, 460 now).
  2. It runs once per session, and the saguaro line sits just before the hitch in both hit sessions (q40, q46 route 1).
  3. The "skinned" line appears in every session, including the ones without a logged hitch (q46 route 2, q47, q48).
- **Why it looked Bench-only and random:** the skin lands within a second or two of the townwalk stop starting. The runner only logs hitches in the measured window, so whether it was counted depended on that timing. In q48's 8 sessions it always fell just before the window: the runner's worst was 23-27 ms while the probe saw 460 ms. The q47 Profile runs were the same (skin line present, stop clean). **It is not release-only; the earlier tallies were timing luck.**
- **SurfaceGlow's far scan is cleared:**
  - 11 landings a session, none lined up with a slow frame in the walk.
  - Upload is 2 x 160x160 RGBA32F = 800 KB per landing.
  - The scan itself runs 460-790 ms but on a worker. The 780 ms first scan is during load.
- **Not proven:** the probe times player loop phases, not single scripts. The saguaro is the only thing logged in that frame and the timing matches every session, but a profiler capture of that exact frame would settle it. A Profile run with -profilestop on herewarm (where the skin lands) can do that if wanted.
- **Suggested fix (animal lane, ColossusRigs.cs):** move the skin build off the main thread (Burst job, or spread over frames), or bake it ahead. The tortoise and mesa tortoise rigs use the same pattern ("tortoise skinned", "mesa tortoise skinned"), so they probably hitch the same way when first approached.

**Also seen at load (expected, for the record):**
- The 12 s island bake frame: InitializationSystemGroup 9.6 s plus MonoBehaviour Update 1.9 s.
- Then 1.4 s, 0.8 s and 0.35 s frames in PresentationSystemGroup (first creature and flora uploads) before "world ready".

**Method:**
- New runner switch `-benchprobe [ms]` (branch b/bench-probe, one commit on main, Tools/bench only; for review). It times every second-level player loop system with a stopwatch, works in release builds, and on any frame over the threshold logs:
  - the 8 slowest systems;
  - GC gen0 collections;
  - SurfaceGlow far-scan state.

  It also logs each far-scan landing with its texture upload size.
- The first probe build never hooked the frame start (the first top-level loop phase has no sub-systems in Unity 6). That's fixed. Those 4 sessions (q48logs) are only a count: no runner hitch over 100 ms.
- Bench build of 781fa5a with the probe, v2 townwalk, uncapped, 4 sessions.
- Script: q48.sh. Logs: results/q48blogs.
- Cost: 14 calls, peak 265k context, $1.5

## 2026-11-06 Millfield townwalk big frame: not reproduced in the Profile build, main 781fa5a

**Short answer:**
- **4 Profile sessions of the v2 townwalk, none over 36 ms** (worst 33.4-35.8 ms). So there is no capture of the big frame.
- Over all runs so far, the frame shows **only in the Bench (release) build**: 2 of 3 Bench sessions (q40 331 ms, q46 418 ms), 0 of 5 Profile sessions (q40's townwalk capture plus these 4).
- **Both hits were at exactly the same spot: (3253,10,-857), on the east side of Millfield.** The route times differed (89.3 s and 59.9 s), so the trigger is the camera position, not time.
- **The log shows nothing loading at that moment.** Around the 418 ms frame there are only FolkChat lines and routine [farshadow] timings. No [stream] batch, bake, dungeon or rig line.
- It fits **something that runs once when the camera crosses a threshold near there and finishes faster in the Profile build's timing, or overlaps a background job differently.**
  - One candidate is position based: SurfaceGlow's far map, the same system as the quit crash. It rescans when the camera has moved MoveBeforeFarRescan from the last scan, and uploads two textures on the main thread when the scan lands.
  - That's a guess, not shown.
- **Quit crash:** not seen in these 4 Profile runs (q46 hit it once).

**Next options (your call):**
1. **Bench timing probe:** a few more Bench sessions with the runner logging, on any frame over 200 ms, which known per-frame systems ran long. The bench runner can read the game's own stopwatch fields (CaveSystem's slow-frame marks, SurfaceGlow.FarScanMs, the stream batch timings) without a profiler. That's runner-only tooling, my lane.
2. Ask the world lane what sits or streams in at (3253,-857) on v2 (east Millfield).
3. Park it: it's once per walk, at one spot, and only in release timing.

**Method:** Profile build of 781fa5a, v2 route stop townwalk alone, uncapped, the last 2000 frames of each stop captured. The route log's worst frame covers the whole stop. BigFrame listed frames over 100 ms (none). Script: q47.sh. Logs: results/q47logs.
- Cost: 6 calls, peak 250k context, $0.6

## 2026-11-06 FaunaDirector recheck after PR #172 (time-sliced comfort refresh), main 781fa5a: fixed; plus a shutdown crash

**Short answer:**
- **PR #172 fixed the FaunaDirector hitches.** Join/leave and running are back to job 28 levels or better:

  | | job 28 (6f139d0) | q40 (adb11e0) | now (781fa5a, 2 sessions) |
  |---|---|---|---|
  | join/leave host: frames over 33 ms | 2 | 60 | **3 / 2** |
  | join/leave host: 1% low fps | 55.5 | 31.1 | **63.1 / 54.3** |
  | join/leave host: worst ms | 34.9 | 43.3 | 33.6 / 33.4 |
  | join/leave client: frames over 33 ms | 0 | 26 | **0 / 0** (1% low 81-82 fps) |
  | run: frames over 33 ms | 3 | 24 | **0 / 0** |
  | run: avg fps / 1% low / worst ms | 100.1 / 36.5 / 34.7 | 88.4 / 22.2 / 76.3 | **121.0 / 54.0 / 25.4** and 118.1 / 56.7 / 29.7 |
  | glide: frames over 33 ms | 64 | 23 | **0 / 1** (avg 97.5 / 86.6 fps) |

- **The townwalk big frame is real and tied to a place.** Session 1 had a **418 ms** frame at exactly the same spot as q40's 331 ms one: Millfield, (3253,10,-857), on the v2 townwalk. Session 2 didn't hit it, at 29.8 ms worst, so it doesn't happen every time. Something there loads or builds once, probably a building, interior or door near that point. That is the next thing to capture. **World lane, once captured.**
- **CRASH, stopped per the crash rule.** It came in the first Profile run (the run stop) **after the stop had finished ("BenchRoute: done")**, so during quit. It's a native crash in a Burst job:

  ```
  TerrainSource.PondDig            (Terrain/TerrainSource.cs:417)
  TerrainSource.SampleHeight       (Terrain/TerrainSource.cs:674)
  FloraPlacement.Place             (Flora/FloraPlacement.cs:1690)
  SurfaceGlow.FarScanJob.Execute   (IJob, worker thread)
  ```

  - Line 417 reads the biome blob (`ref var biome = ref set.Biomes[core.Ids[i]]`). So the likely cause is SurfaceGlow's far scan job (W-101 row 1, 43747a5d) still running at quit after the TerrainSource blobs were freed.
  - SurfaceGlow.Release() completes the job, but only when Release runs. If the terrain's blobs are disposed first, the job reads freed memory.
  - **First time seen** in any bench log on this laptop (q27-q46). It's a shutdown crash, so players would see a crash on quit, not mid-game.
  - Suggested owner: world lane (SurfaceGlow / terrain teardown order). The fix: complete the far job before the terrain blobs are disposed, or have the job's owner dispose them.
- **Not run because of the stop:** the 3 Profile captures (run, v2 fishing village, salt walk-up) that would have confirmed FaunaDirector is gone from the spike frames. The Bench numbers above already show the hitches gone. I can rerun the captures when you say.

**Method:**
- Bench build of 781fa5a (#172 + #173), uncapped, frames over 33 ms logged.
- The v2 route stops townwalk, run and glide; the host + client join/leave at the v2 cave (210 animals). 2 sessions each.
- Script: q46.sh. Logs, including the crash log prof46-run.log: results/q46logs.
- Cost: 12 calls, peak 240k context, $1.1

## 2026-11-06 creature render breakdown at the v3 forest spot, main adb11e0

**Short answer:**
- **Creature rendering is cheap on the GPU here: about 0.15 ms** (Profile build: 6.35 -> 6.2 ms with creatures off). q41's 0.85 ms came from the Bench build's GPU timer, which the water test (q43) already showed is unreliable. With shadows off the creature share is about 0.05 ms; at a quarter of the pixels, about 0.25 ms. All three are within noise of each other.
- **CPU: 0.80 ms per frame across threads, 0.22 ms of it on main.**
  - The game's pass log puts the main-thread part at 0.21 ms: extras 0.07, cull 0.04, anchors 0.04, upload + draw 0.03.
  - The rest (about 0.6 ms) is the Burst pose job on workers.
  - Creatures off saves about 0.3 ms of frame (main 9.7 -> 9.4 and 8.2 -> 8.0; about 3 fps).
- **Counts:** about 90 creatures loaded; 29-38 kept after the behind and frustum cull; 1,100-1,450 body segments drawn (1,190-1,660 with extras).
- **Draws:** 4 instanced draws (near, mid and far bands, plus a no-shadow band for colonies and traces), each submitted in every pass that takes them.
  - The counters show **7,078 instances and 1.17 M triangles a frame from creatures** (instanced draws 7,108 vs 30 and triangles 2.21 M vs 1.04 M, plain vs creatures off).
  - That's about 5 draws of every segment: the depth prepass, the main pass, and **all 3 shadow cascades**.
- **Far creatures do not draw at full detail.** There are three detail bands by distance:
  - near, under 40 m: 336 triangles a segment;
  - mid, 40-100 m: 112;
  - far, over 100 m: 48.
- **What far creatures still do:**
  1. **No distance cutoff**: any creature in front of the camera is drawn, however far.
  2. **Every band casts shadows**, including far creatures well past the 50 m shadow distance. Each band is one instanced draw with 2 km bounds, so Unity can't cull single instances out of cascades. Every segment goes into every cascade. That's where the about 5 draws a segment come from.
- **Optional small fix (animal lane, CreatureRenderSystem.Passes.cs, not a free file):** draw the far band with ShadowCastingMode.Off, the way colonies already are. Past 100 m it is beyond the 50 m shadow distance anyway. This removes up to 3 of every far segment's 5 draws.
  - At today's cost (0.15 ms GPU) it isn't worth a card on its own. It matters if creature counts or segment counts grow, so it fits the roadmap's bigger crowds.
- **Not over 0.5 ms on the GPU.** The q41 flag for creature render stands only for CPU (0.8 ms, mostly worker pose time).

**Method:**
- Profile build adb11e0p2, v3, 150 m east of the CinderSnail (2349,-2575), uncapped, 20 s.
- Creatures off (-benchnocreatures: neither posed nor drawn) against plain, alone, with camera shadows off, and at render scale 0.5.
- 2 rounds, order reversed; -renderpasslog on every run. Round 1 plain and creatures off were captured through SysMs, which costs a few fps in round 1.
- Band triangle counts are from FaunaDirector.BuildCapsule(12,6,4 / 8,3,2 / 6,2,1); band distances are PoseNear 40 and PoseFar 100.
- Script: q45.sh. Logs: results/q45logs.

| variant | GPU ms with creatures (r1 / r2) | GPU ms creatures off (r1 / r2) | creature share |
|---|---|---|---|
| plain | 6.4 / 6.3 | 6.3 / 6.1 | 0.15 |
| camera shadows off | 6.3 / 6.3 | 6.3 / 6.2 | 0.05 |
| render scale 0.5 | 5.1 / 5.1 | 4.8 / 4.9 | 0.25 |

| counter (per frame) | plain | creatures off |
|---|---|---|
| instanced draws | 7,108 | 30 |
| triangles | 2.21 M | 1.04 M |
| shadow casters | 109 | 100 |
| SetPass | 436 | 420 |

- Cost: 10 calls, peak 225k context, $1.0

## 2026-11-06 flora breakdown at the v3 cave approach: passes, and vertex vs pixel bound, main adb11e0

**Short answer:**
- **Flora costs 2.4 ms GPU here** (8.1 -> 5.7 ms with flora shaders off; q41 saw 2.0 in the Bench build).
  - **About 1.0 ms is the shadow casters.** Shadows cost 1.2 ms with flora and 0.2 ms without. Flora is 307 of the 334 shadow casters.
  - **About 1.4 ms is the depth prepass plus the main pass.** The PC renderer is Forward+ with **depth priming forced**, so flora is drawn in three passes: depth prepass, main (depth-equal) and shadows.
- **With depth priming on, flora is vertex/draw bound, not pixel bound.** At a quarter of the pixels (render scale 0.5), flora still costs 2.65 ms (6.7 vs 4.05), while the rest of the scene drops from 5.7 to 4.05 ms.
- **Depth priming is what keeps flora's pixel cost down.** With priming off, the GPU frame goes to **22.0 ms** (39 fps) with flora and 6.8 ms without, so flora's cost becomes about 15 ms. Its fragment shading has heavy overdraw (alpha-tested leaves), which priming culls to one shaded pixel. **Don't turn priming off.**
- **Triangles: the counter doesn't see flora.** It reads 4.45 M with flora and 4.27 M without, because FloraSystem draws through Graphics.RenderMeshIndirect and indirect draws aren't counted. Instanced draws (9.6k / 10.5k) and SetPass (231 / 224) barely change. Flora's real vertex count would need FloraSystem's own instance x mesh counts. The render-scale result already says it's vertex/draw bound.
- **Where to cut (world lane):**
  1. **Flora vertex work, all passes:** fewer vertices per instance at distance (mesh LODs or impostors past some range). Every vertex is processed 3 times (prepass, main, up to 3 shadow cascades).
  2. **Shadow casters:** about 1 ms. Cut by limiting flora to the near cascades, or by a cheaper shadow mesh. The cheap far caster (PR #158) already helps; this spot still pays 1 ms.
  3. **Main pass:** with priming the main pass shades only visible pixels. Splitting prepass from main would need a GPU-pass timer this D3D11 build doesn't give.
- **No self-fix:** flora is in the world lane's files. The runner gained two switches for this job (bench tooling only), pushed as b/bench-noprime-scale (one commit on main) for review:
  - `-benchnoprime`: depth priming off on every URP renderer.
  - `-benchrenderscale <x>`: URP render scale.

**Method:**
- Profile build of adb11e0 with the new switches, v3 route stop caveapproach (a walk from 400 m to 100 m from the biggest cave), uncapped.
- Each pass was switched off with and without flora (-benchnoshader Flora). 2 rounds, order reversed; tables give the mean.
- Round 1 plain and flora-off were also captured through SysMs for the render counters. Round 1 plain read 8.4 ms GPU with the capture running, against 7.8 uncaptured.
- Script: q44.sh. Logs: results/q44logs.

| variant | fps | median ms | GPU ms (with flora) | GPU ms (flora off) | flora's share |
|---|---|---|---|---|---|
| plain | 85.4 | 11.4 | 8.1 | 5.7 | **2.4** |
| camera shadows off | 92.6 | 10.7 | 6.9 | 5.5 | 1.4 (so shadows hold 1.0) |
| depth priming off | 39.5 | 25.2 | **22.0** | 6.8 | **15.2** |
| render scale 0.5 | 103.5 | 9.4 | 6.7 | 4.05 | 2.65 |

- Round-to-round GPU spread: 0.2 ms or less, except plain (8.4 captured vs 7.8).

| counter (per frame) | with flora | flora off |
|---|---|---|
| triangles (indirect draws not counted) | 4.45 M | 4.27 M |
| instanced draws | 9572 | 10511 |
| SetPass | 231 | 224 |
| shadow casters | 334 | 27 |

- Cost: 9 calls, peak 210k context, $0.9

## 2026-11-06 finding 11 follow-up 2: where the village water's frame time goes, main adb11e0

**Short answer:**
- **Water is real render work, not an idle wait.** In the Profile captures at v3 fishing village 1, water costs:
  - **GPU 2.1 ms** (7.05 -> 4.85 ms with water shaders off). The Bench build's GPU timer showed only 0.3 ms for the same switch, so that timer under-reads it. Trust the frame and fps drop (1.5 ms, 101 -> 117 fps) over the Bench GPU figure.
  - **About 1.4 ms of render jobs on worker threads** (EngineJob inclusive 5.10 -> 3.68 ms), spread over:
    - RenderLoop.DrawSRPBatcher (inclusive 1.39 -> 0.75);
    - SRPBatcher.Flush (0.88 -> 0.46);
    - RenderLoop.Sort (0.85 -> 0.51);
    - SRPBatcher.DrawCommandsInjection (0.89 -> 0.72).
  - **0.6 ms on the render thread** (3.1 -> 2.5; DrawBuffersBatchMode 1.00 -> 0.68).
  - **0.15 ms of DXGI.WaitOnSwapChain on main** (0 with water off): the CPU waiting for the GPU to present. That's the visible tip of the GPU cost. In the Bench build, where the main thread is shorter, it becomes the 1.5 ms gap between frame and main thread.
- **The extra sort and SRP-batcher flush time** suggests water is drawn as many separate transparent objects (sorted back to front, each breaking a batch), not as one or a few draws. That's a guess from the markers. A water renderer and draw count would confirm it.
- **Suggested card, world lane:**
  1. Count the water draws at the village.
  2. If it's many tiles or chunks, merge them, or draw them as one batched (instanced, or in the opaque-after-opaques queue) set.
  3. Check the water shader's per-pixel cost (2 ms GPU for a coastline is a lot on this laptop).
- The opaque/depth copies were already ruled out (q42).

**Method:**
- Profile build of adb11e0, v3 fishing village 1 (-2151,3731), uncapped, 20 s fully captured.
- Plain vs -benchnoshader Water, 2 rounds each (A B B A). Every capture went through BProfileTools.Analyze. The diff is the mean of the 2 plain runs minus the mean of the 2 water-off runs, per marker.
- The Profile build is main-thread bound (10.6 ms), so its frame moves only 0.35 ms (10.9 -> 10.5 mean). The work and GPU deltas are the useful part.
- Script: q43.sh. Analyses: results/q43logs.

| run | fps | median ms | GPU ms | main ms | render thread ms |
|---|---|---|---|---|---|
| plain 1 / 2 | 91.7 / 91.6 | 10.5 / 10.5 | 7.0 / 7.1 | 10.7 / 10.7 | 3.1 / 3.1 |
| water off 1 / 2 | 94.5 / 95.9 | 10.2 / 10.1 | 4.9 / 4.8 | 10.6 / 10.4 | 2.5 / 2.5 |

- Cost: 6 calls, peak 200k context, $0.5

## 2026-11-06 finding 11 follow-ups: village water and GC.Alloc callers, main adb11e0

**Short answer:**
- **The opaque/depth copies are not the water cost.** At v3 fishing village 1, copies off changes nothing (101 fps, 9.8 ms). Water off gives 116.7 fps, 8.3 ms, and copies off on top adds nothing.
- With water off the frame falls 1.5 ms, but GPU only 0.3 ms (5.3 -> 5.0), the render thread 0.5 ms (2.7 -> 2.2), and the main thread not at all (8.5).
  - With water on, the frame (9.8 ms) is longer than the main thread (8.5 ms). With water off they match (8.3 / 8.4).
  - So water adds a wait the timers don't attribute: likely a GPU or present sync rather than work.
  - Next step: Profile captures, plain vs water off, comparing the main-thread wait markers. About 20 minutes; I can run it.
- **GC: two callers make about 60% of the 10-12 KB a frame, at all three spots:**
  - **IMGUI (GUIUtility.BeginGUI): 3.3 KB, 36 allocations a frame.** Unity's OnGUI pass allocates every frame while any MonoBehaviour has an OnGUI, even one that draws nothing. 11 game scripts have OnGUI, among them Items, Doors, Interiors, FolkTalkSystem, InteractionInputSystem, TempleSockets, FrameRateCap, NetMenu, MainMenu and DebugControls (and the bench runner in bench builds).
  - **Items.Update: 3.1-3.4 KB, 30-35 allocations a frame.** It runs about 12 steps a frame (chests, net serve/receive, target search, quests, tests, inventory keys, draw). The capture can't say which one allocates. The deep-profiling run that would have shown it timed out (see below).
- Smaller, all under 1.5 KB a frame:
  - FaunaDirector.Update 0.8-1.4 KB (10-19 allocations; most at the village).
  - FloraSystem.LateUpdate 0.2-0.9 KB (0.9 at the cave approach).
  - AmbientParticleSystem 0.7 KB (cave approach only, 1.7 allocations, so a few big ones).
  - WeatherSystem 0.35-0.67 KB, CaveSystem 0.3-0.45 KB, FolkTalkSystem about 0.3 KB, CreatureBurrowSystem 0.3 KB.
  - InteractionPrompt.OnGUI 0.2 KB, LandmarkEffectSystem 0.2 KB.
- **No self-fix:** Items.cs and the OnGUI scripts are other lanes' files and busy. The fix for both is clear enough for a card:
  1. Items: find and cache the per-frame allocation.
  2. OnGUI: disable the component, or set useGUILayout = false, when a script has nothing to draw. Every frame they all pay the IMGUI layout pass.

**Water test:** Bench build, v3 fishing village 1 (-2151,3731), uncapped, 20 s, 3 rounds, order rotated. Means of 3:

| variant | fps | median ms | GPU ms | main ms | render thread ms |
|---|---|---|---|---|---|
| plain | 100.6 | 9.8 | 5.4 | 8.5 | 2.7 |
| copies off (-benchnocopies) | 100.8 | 9.8 | 5.3 | 8.5 | 2.7 |
| water off | 116.7 | 8.3 | 5.0 | 8.4 | 2.2 |
| water + copies off | 116.2 | 8.4 | 5.0 | 8.4 | 2.2 |

- Spread across rounds: 1.2 fps or less, so this is solid.

**GC callers, bytes / allocations per frame (Profile build, whole stop captured; parent = the nearest profiler sample above each GC.Alloc):**

| caller | forest | village | cave approach |
|---|---|---|---|
| IMGUI GUIUtility.BeginGUI | 3310 / 36 | 3310 / 36 | 3310 / 36 |
| Items.Update | 3144 / 30 | 3442 / 35 | 3182 / 31 |
| FaunaDirector.Update | 793 / 10 | 1447 / 19 | 787 / 10 |
| WeatherSystem.Update | 352 / 5 | 672 / 9 | 352 / 5 |
| FloraSystem.LateUpdate | 203 / 7 | 204 / 7 | 919 / 8 |
| AmbientParticleSystem.Update | - | - | 738 / 1.7 |
| CaveSystem.Update | 304 / 4 | 304 / 4 | 446 / 5 |
| FolkTalkSystem (ECS) | 343 / 9 | 280 / 7 | 283 / 7 |
| CreatureBurrowSystem (ECS) | 293 / 4 | 293 / 4 | 293 / 4 |
| LandmarkEffectSystem.Update | 212 / 4 | 213 / 4 | about 228 / 4 |
| InteractionPrompt.OnGUI | 204 / 6 | 204 / 6 | 235 / 6 |
| **total** | **9.7 KB / 132** | **10.8 KB / 146** | **12.1 KB / 139** |

- **Deep profiling didn't work here.** The BuildDeep build with -deepprofiling never reached its stop in 900 s (load is too slow under deep profiling), so there are no exact call sites. A dedicated attempt would need a much longer timeout, or deep profiling switched on only for the stop.
- Script: q42.sh. Tool: GcCallers.cs.txt (lanes/profiler/scripts). Logs: results/q42logs.
- Cost: 17 calls, peak 191k context, $1.2

## 2026-11-06 audit finding 11: v3 per-system sweep (ms per system, GC, draw counters), main adb11e0

**Short answer (flags over 0.5 ms):**
- **Flora is the biggest single cost.**
  - Cave approach: GPU 2.0 ms (fps 86 -> 114 with flora shaders off), CPU 1.24 ms (FloraSystem.LateUpdate 0.78 ms on main).
  - Forest: GPU 1.15 ms, CPU 0.50 ms.
  - Village: GPU 0.45 ms, CPU 0.56 ms.
- **Creature brain: 0.8-1.7 ms** of job time per frame (1.7 at the fishing village). Almost all of it is on workers (main thread 0.05 ms), so it costs frame time only when the workers are full.
- **Creature render: 0.8-1.5 ms CPU** (0.26-0.42 ms of that on main), plus GPU 0.85 ms in the forest, 0.4 at the village and 0.25 at the cave.
- **URP shadows: 0.8-1.7 ms CPU**, on the render thread and workers. GPU 0.3 ms (forest), 0.5 (village), 1.35 (cave approach).
- **Colossi: 0.54 ms at the cave approach**, mostly ColossusWalker.Update at 0.43 ms on main (a walker near the biggest cave). It's 0.28 ms 150 m from the CinderSnail and 0.15 at the village. Colossus GPU is 0 within noise everywhere.
- **Water at the fishing village: frame 9.8 -> 8.3 ms (100 -> 117 fps) with water shaders off**, but measured GPU drops only 0.4 ms and the main thread doesn't move. Something about water costs about 1.5 ms that the GPU timer doesn't show (the opaque/depth copies are the first suspect; not tested). Water is 0 at the other two spots, and water's CPU is 0.015 ms. **World lane.**
- **Under 0.5 ms everywhere:**
  - Ambient life 0.15-0.32 ms (AmbientParticleSystem; GPU about 0.1).
  - Critters 0.04-0.11 ms.
  - Far shadows 0.02 ms CPU (land frame 0.07 ms; GPU inside the colossus figure, 0).
  - FaunaDirector 0.18-0.27 ms on average (but see the 1-2 s spikes in the q40 entry).
  - Creature audio 0.1-0.18 ms. Landmarks 0.05 ms.
- **GC: 10-13 KB and 132-151 allocations every frame**, at all three spots. That's about 1 MB/s at 100 fps (1-9 collections per stop). Not flagged by ms, but steady per-frame allocation is worth finding. SysMs doesn't attribute it yet.
- **Draw calls:** "Draw Calls Count" and "Batches Count" aren't recorded on this D3D11 build. What is recorded, per frame:

  | spot | SetPass | instanced draws | shadow casters | triangles |
  |---|---|---|---|---|
  | forest | 438 | 6.3-7.3k | 109 | 2.2-2.4 M |
  | village | 139 | 5.2-6.6k | 262 | 2.4-2.8 M |
  | cave approach | 230 | 8.8-9.6k | 334 | 4.1-4.3 M |

- **Frame:** forest 118 fps, village 101 fps, cave approach 86 fps (Bench build). All three are main-thread or present bound except the cave approach, where flora's GPU cost shows. No frames over 50 ms in 42 Bench runs and 6 Profile runs.

**Method:**
- v3 island, uncapped, main adb11e0. Spots:
  - Forest: 150 m east of the Ember Forest CinderSnail (2349,-2575).
  - Village: v3 fishing village 1 (-2151,3731).
  - Cave approach: the route stop caveapproach, a walk from 400 m to 100 m from the biggest cave.
- **CPU:** Profile build, the whole stop captured, 2 rounds. Each capture went through a new editor tool, SysMs.cs.txt (lanes/profiler/scripts). A system's ms is the inclusive time of its outermost samples per frame, summed over all threads; the main-thread share is shown separately. GC is the GC.Alloc samples. Counters are averaged over every frame.
- **GPU:** Bench build. Each system's shaders were switched off in turn (water; flora; creatures; colossus + far shadow; particles, ambient and critters; camera shadows), and its cost is that run's gpu ms against a plain run. 2 rounds, order reversed. The tables use the mean of both rounds; round-to-round GPU spread was 0.1-0.2 ms.
- **Limits:** AmbientLife and FarShadow aren't profiler markers, so their CPU cost sits inside their callers. Creature brain includes BrainJob, which runs on workers.
- Script: q41.sh. Logs and SysMs outputs: results/q41logs.

**CPU, ms per frame (all threads / main), mean of 2 captures:**

| system | forest | village | cave approach |
|---|---|---|---|
| flora | 0.50 / 0.50 | 0.56 / 0.56 | **1.24 / 0.78** |
| creature render | **0.78** / 0.26 | **0.99** / 0.31 | **1.45** / 0.42 |
| creature brain | **0.78** / 0.05 | **1.74** / 0.05 | **1.55** / 0.05 |
| URP shadows | **0.77** / 0.07 | **1.53** / 0.08 | **1.69** / 0.07 |
| colossi | 0.28 | 0.15 | **0.54** |
| ambient life | 0.25 | 0.15 | 0.32 / 0.27 |
| fauna director | 0.18 | 0.26 | 0.25 |
| ground detail | 0.10 | 0.09 | 0.32 / 0.12 |
| creature audio | 0.09 | 0.17 | 0.12 |
| critters | 0.04 | 0.05 | 0.11 |
| landmarks | 0.05 | 0.05 | 0.05 |
| far shadows | 0.02 | 0.02 | 0.02 |
| water / rivers | 0.02 | 0.01 | 0.01 |
| GC per frame | 10.2 KB, 136 allocs | 11.2 KB, 147 | 12.4 KB, 147 |

- Other main-thread scripts at 0.1-0.4 ms on every spot: WeatherSystem.Update 0.25-0.35, CaveSystem.Update 0.17-0.41, Items.Update 0.19, Mantrap.Update 0.16. Mantrap ticks every plant each frame, even far from any; that's small and not flagged.
- The ECS groups (SimulationSystemGroup 1.1-1.3 ms, PresentationSystemGroup 0.8-1.2 ms) contain the creature render and brain scheduling above.

**GPU, ms saved with the system's shaders off (plain gpu: forest 6.2, village 5.35, cave approach 7.5):**

| switched off | forest | village | cave approach |
|---|---|---|---|
| flora | **1.15** | 0.45 | **2.0** |
| creatures | **0.85** | 0.4 | 0.25 |
| camera shadows | 0.3 | 0.5 | **1.35** |
| water | 0.3 | 0.4 (**frame -1.5 ms**) | 0 |
| colossus + far shadow | 0 | 0 | 0 |
| particles / ambient / critters | 0.1 | 0.1 | 0 |

**Follow-ups I'd suggest (not done; measure-only job):**
1. Village water: rerun with -benchnocopies to see whether the opaque/depth copies are the hidden 1.5 ms. Profiler lane, can run next.
2. Per-frame GC: attribute the 140 allocations a frame to callers (BigFrame-style call stacks on GC.Alloc). Profiler lane.
3. ColossusWalker 0.43 ms near the cave: check which walker and why. Animal lane.

- Cost (both 11-06 sweeps, q40 + q41): 48 calls, peak 161k context, $4.0

## 2026-11-06 idle sweep (Zach): frames over 33 ms on main adb11e0

**Short answer:**
- **No frames of 0.5 s or more, no hangs, no crashes, no exceptions** across the v2 route, 6 village spots, the salt colossus walk-up and a host + client join/leave.
- **One 331 ms frame**: v2 townwalk in Millfield (3253,-857), 89 s into the route. It wasn't repeated in the townwalk capture (worst 37 ms), and the log shows nothing around it. Cause unknown; I'll watch for a repeat.
- **FaunaDirector.Update is in the slow frames of all 4 captures**: 10-28 ms on those frames against 0.25-0.35 ms normally. It looks periodic, every 1-2 s (fishing village: 1.0, 2.0, 15.1, 17.1 s; salt walk-up: 28.2, 30.2, 32.2 s). This is the biggest main-thread spike in the sweep. **Animal lane** (FaunaDirector is too big and busy for a self-fix).
- **Join/leave got worse than job 28** (main 6f139d0): the host now logs 60 frames over 33 ms (was 2), worst 43 ms, 1% low 31 fps (was 56). The client logs 26 (was 0). They come about once a second, which fits the FaunaDirector tick (224 animals at the cave).
- **Fast movement:**
  - Run: 24 frames of 36-76 ms (job 28: 3); worst 76 ms (was 35), 1% low 22 fps (was 37).
  - Glide: better than job 28: 23 frames of 34-65 ms (was 64), avg 77 fps (was 59).
  - Glide slow frames: BuildChunkMeshJob 45 ms on a worker (normally 13), FloraSystem PlaceJob 11 ms, SurfaceGlow ScanJob 8 ms, FaunaDirector 10 ms. Streaming while moving fast is still the open item.
- **Villages are fine:** v3 fishing villages 1-4 at 92-106 fps with 0-3 frames over 33 ms (worst 40 ms). v2 fishing village 73 fps, desert village 112 fps, salt walk-up 114 fps.

**Method:** q28's sweep, retargeted (q40.sh). Bench build, uncapped, frames over 33 ms logged:
- the v2 route stops;
- every v2/v3 fishing village and the v2 desert village, 30 s each;
- the v3 salt crystal colossus walk-up;
- a host + client join/leave at the v2 cave.

Then Profile captures (60 fps cap so 2000 frames cover the stop) of townwalk, glide, the v2 fishing village and the salt walk-up, through BigFrame and BProfileTools. The fish wharf run was dropped because -place prop: is broken (#137). Logs: results/q40logs.

| stop | avg fps | 1% low | median ms | worst ms | frames over 33 ms (job 28) |
|---|---|---|---|---|---|
| town | 100.0 | 53.1 | 9.9 | 21.9 | 0 |
| townwalk | 96.4 | 28.0 | 10.0 | 331.1 | 6 (0) |
| forest | 91.6 | 36.4 | 10.7 | 37.0 | 1 (0) |
| cave | 171.0 | 44.3 | 5.5 | 30.5 | 0 |
| lake | 92.6 | 32.5 | 10.6 | 33.5 | 1 (0) |
| river | 89.8 | 36.7 | 11.0 | 31.0 | 0 |
| run | 88.4 | 22.2 | 9.8 | 76.3 | 24 (3) |
| glide | 77.0 | 22.8 | 11.6 | 65.3 | 23 (64) |
| dash | 89.0 | 40.6 | 11.1 | 29.9 | 0 (1) |
| join/leave host | 114.1 | 31.1 | 8.5 | 43.3 | 60 (2) |
| join/leave client | 123.7 | 32.3 | 7.6 | 39.3 | 26 (0) |

- Colossus placements on v3 this build: 12, including the new CinderSnail at (2199,-2575).
- Cost: included in the audit finding 11 entry above.

## 2026-11-05 job 34: new shadow defaults (PR #158) vs the old default, main c19e799

**Short answer:**
- The new default (cheap far caster + cached far shadows, near 25 m) cuts the **shadow passes' GPU cost by about half where flora shadows matter**:
  - Fishing village: 3.5 -> 1.5 ms, GPU 12.1 -> 10.1 ms, +7.6 fps (67.1 -> 74.8).
  - Swamp spot: 4.3 -> 2.2 ms, GPU 12.5 -> 10.4 ms, +5.3 fps (68.7 -> 74.0).
- **Forest and desert village: no real difference** (0.2 ms or less). Shadows there cost about 1 ms either way.
- **CPU shadow cost is unchanged**: the render thread's Shadows.Draw is the same in new and old at every spot. The saving is all GPU.
- **Hitches: none over 300 ms** in 24 Profile runs and 2 sweep sessions. Worst frame 62 ms (swamp, old, round 1).
- **Stall check: 0 frames of 0.5 s, 0 hangs**, both sweep sessions finished. That makes 6 clean BenchTune sessions since PR #159.

**Method:**
- Profile build of main c19e799, v2 island, uncapped (-fpscap 0), hitch log at 300 ms. 20 s measured at each stop (forest: the route stop).
- Variants: new (no flags), old (`-cheapfarcaster 0 -cachedfarshadows 0`), and no camera shadows (`-benchnoshadows`). Shadow-pass GPU = variant - no shadows.
- 2 rounds, order reversed in round 2. Tables give the mean of both. Round 1 new and old were also captured for the CPU markers.
- Then a Bench build of c19e799, 2 BenchTune sweep sessions at the swamp spot.
- **Spot note:** job 33's "fv1" (-3304,-1644) is now a swamp forest on v2. The only fishing village on v2 is at -2883,-2121, so both are reported. Desert village: the Sonoran HumanVillage at 3045,-827.
- Script: q38.sh. Logs: results/q38logs.

| spot | variant | fps | median ms | GPU ms | shadow-pass GPU ms |
|---|---|---|---|---|---|
| fishing village | new | 74.8 | 13.1 | 10.1 | 1.5 |
| | old | 67.1 | 14.6 | 12.1 | 3.5 |
| | no shadows | 78.5 | 12.5 | 8.6 | - |
| swamp (job 33 "fv1") | new | 74.0 | 13.3 | 10.4 | 2.2 |
| | old | 68.7 | 14.4 | 12.5 | 4.3 |
| | no shadows | 81.4 | 11.9 | 8.2 | - |
| forest | new | 90.8 | 10.8 | 6.6 | 0.9 |
| | old | 88.3 | 11.4 | 6.8 | 1.1 |
| | no shadows | 96.9 | 10.1 | 5.7 | - |
| desert village | new | 101.9 | 9.7 | 7.5 | 0.8 |
| | old | 102.0 | 9.8 | 7.6 | 0.9 |
| | no shadows | 112.0 | 8.9 | 6.7 | - |

- Round-to-round spread: GPU within 0.6 ms (swamp new 10.1 / 10.7, forest new 6.9 / 6.3). The fishing and swamp new-vs-old gaps (2.0-2.1 ms) are well outside that.
- Fishing and swamp are still GPU-light compared with the frame (main thread about 9-11 ms, frame 13 ms), so frame time falls by about 1.1-1.5 ms, not the full GPU saving.

**CPU shadow markers (round 1 captures, ms per frame, mean):**

| spot | Render Shadows.Draw new / old | Workers Shadows.Draw new / old | MainLightShadow new / old |
|---|---|---|---|
| fishing | 0.59 / 0.59 | 0.40 / 0.40 | 0.65 / 0.65 |
| swamp | 0.62 / 0.61 | 0.41 / 0.41 | 0.68 / 0.67 |
| forest | 0.82 / 0.80 | 0.51 / 0.51 | 0.89 / 0.87 |
| desert | 1.37 / 1.33 | 0.75 / 0.76 | 1.46 / 1.42 |

- The desert village has the highest CPU shadow cost (1.4 ms render thread, buildings) but the lowest GPU shadow cost.

**Sweep (Bench build, swamp spot, GPU ms, session 1 / 2):**

| set | GPU ms |
|---|---|
| new (default) | 12.29 / 12.24 |
| old (cheap=0, cached=0) | 13.58 / 13.71 |
| Option 1: x0.5, 2 cascades | 12.21 / 12.16 |
| No flora shadows | 11.79 / 11.92 |
| Cheap only | 12.65 / 12.79 |
| Cached only | 13.37 / 13.69 |
| New + still | 12.86 / 12.94 |

- In-session, new beats old by 1.4 ms and is within 0.4 ms of no flora shadows. Cheap does most of the work; cached adds about 0.4 ms on top.
- Still far shadows still cost about +0.6 ms over the new default. Keep them off.
- The sweep's absolute GPU is about 2 ms higher than the Profile runs at the same spot (a different build and a longer stop). Compare within the table, not across.
- 0.5 s frames: 0 and 0. Hangs: 0. Hitches over 300 ms: none.

- Cost: 29 calls, peak 303k context (session), $2.5

## 2026-11-05 b/fix-stall-hang: the 0.5 s frames and hangs came from the bench runner, not the game

**Short answer:**
- The recurring 510-523 ms frames and the 3 hangs (q33, q34, q35) happened **only in sessions using BenchTune**, the live shadow panel added for job 33.
- BenchTune, its sweep and the route each called `FrameTimingManager.CaptureFrameTimings()`, so during a sweep it ran **up to 3 times a frame**.
- With one capture per frame (b/fix-stall-hang, Tools/bench/BenchmarkRoute.cs.txt only), the same sweep on the same game commit had **no 0.5 s frames and no hangs in 4 sessions**. Worst frame 125 ms.
- The game itself is not implicated, and no dump was needed. Fix for review: branch b/fix-stall-hang (one commit on main a35a4d0).

**Evidence:**
- Earlier bench logs without BenchTune: q27, q28, q29 and q31, 137 logs, 688 hitches over 50 ms, **0** in the 500-529 ms band.
- With BenchTune before the fix:
  - q34 + q35 (sweep): 8 sessions, 8 frames of 508-523 ms (one of 663), 1 permanent hang and 1 452 s freeze.
  - q33 (BenchTune without a sweep): 2 runs that never finished their stop.
- A Profile build with the same sweep (q36, 2 sessions, every frame captured) showed no frame over 300 ms. The stall needs the release build's timing; I didn't chase why.
- After the fix (q37 = q35's sweep, Bench build of b19e37d with the fixed runner): fv1 x2 and forest x2, 0 frames of 0.5 s, 0 hangs. Every session ended normally ("BenchRoute: done").
- **Not proven:** hangs were 3 in about 24 BenchTune sessions, so 4 clean sessions is good but not conclusive. Later sweeps will keep counting.
- Scripts: q36.sh (Profile capture), q37.sh (after). dump.ps1 is ready (dbghelp MiniDumpWriteDump, exact PID, Punchy only, output stays in results/) for a repeat. It was not used.
- Cost: 17 calls, peak 285k context (session), $1.4

## 2026-11-05 job 33 re-run: the shadow sweep after W-107 PR #156 (card holes, cached far shadows), main b19e37d

**Short answer:**
- At fishing village 1, **cheap + cached comes within 0.5 ms of no flora shadows** (11.8 vs 11.3 ms; default 13.3). Adding still changes nothing.
- In the forest, every option is within about 0.5 ms of default (9.3-10.2 ms), so flora shadows barely matter there.
- **Looks:** the cheap far caster's holes fixed the solid blob; its forest shadow is now dappled like default. Cheap + cached gives a broader, flatter patch.
- **Near range 15 m with the cache loses most of the forest's ground shadow** (see the sheet). There's a gap between 15 m and where the cached shadow starts.
- **Still far shadows still cost +0.3 ms** (13.6 vs 13.3 fv1, 10.2 vs 9.9 forest) and save nothing. The world agent says the cost is the per-vertex fade.
- **Recommendation:** cheap + cached at the default 25 m near range. Drop still unless its fade is made cheaper.

**Method:** as job 33 (q34): Bench build, v2, uncapped, BenchTune in-session sweep, 4 s settle + 8 s measured per set, 2 sessions per spot (the second in reverse order). Tables give the mean of both sessions. Script: q35.sh. Sheets: shots/job33/rerun-fv1.jpg (session 2), rerun-forest.jpg (session 1). Logs: results/q35logs.

| setting | fv1 gpu ms (s1 / s2) | forest gpu ms (s1 / s2) |
|---|---|---|
| default | 13.26 (13.15 / 13.36) | 9.93 |
| Option 1: x0.5, 2 cascades | 12.01 | 9.38 |
| No flora shadows | 11.32 | 9.34 |
| Cheap far (with holes) | 12.00 | 9.75 |
| Still far | 13.60 | 10.16 |
| Cached far | 12.65 (12.93 / 12.37) | 9.90 |
| Cheap + still | 12.07 | 9.93 |
| Cheap + cached | 11.83 (12.16 / 11.50) | 9.86 |
| All three | 11.82 (12.23 / 11.41) | 9.88 |
| All three, near 15 m | 11.77 (12.14 / 11.40) | 9.75 |

- The fv1 combinations differ by about 0.8 ms between sessions (order and drift), so the ranking among cheap+cached, all three and all-n15 is within noise. The gap to default (about 1.4 ms) is not.

**Stall repeat (investigation, b/fix-stall-hang):**
- q35 fv1 session 1 **hung for good**: the sweep finished, the log stopped at 13:06:11 (187 s into the stop; the 10 s status line stops too), and the run was killed at the 900 s timeout. This is the third hang in three sweeps (q33 twice, q34 once for 452 s).
- **Signature:** sessions with a hang or near-hang have recurring hitches of **510-523 ms** (q35 fv1-1: 521 @ 52 s, 523 @ 94 s, 510 @ 188 s right before the hang; fv1-2: 521, 510; q34: 514, 508, 520, 663).
  - A wait with a 0.5 s timeout firing repeatedly, then once never returning.
  - The earlier "500 ms stalls" on this laptop were blamed on a firewall dialog. The dialog check is clean now.
- No crash, no exception, nothing in the Windows System log. The Player.log just stops.
- Next step, if you want the slot: a Profile build at fv1 with `-profilestop here` and the hitch log at 400 ms. The 0.5 s frames will then be in the capture, showing what they wait on.
  - A frozen process can't write its last frame, so for the hang itself a dump is the only way. I'd ask Zach before using a dump tool.

- Cost: 14 calls, peak 285k context (session), $1.0

## 2026-11-05 job 33 (Zach): shadow options 1 and 2 vs W-107's far-tree switches, with screenshots

**Short answer:**
- On main a38f9ca (W-102 + W-107), flora shadows cost only **about 1.6 ms of GPU at fishing village 1 and 0.6 ms in the forest**. That's default against no flora shadows. W-102 already took the big share.
- **The cheap far caster gets 70-100% of that back:** cheap + still with a 15 m near range = the no-flora-shadows number.
  - **But its look is wrong in the forest:** the far caster throws a large, solid, dark blob where the default casts a dappled canopy shadow (see the forest sheet).
- **Still far shadows on their own cost more than default** at fishing village 1 (+0.6 ms, 13.7 vs 13.1 ms). They're neutral in the forest.
- Option 1 at x0.5 / 2 cascades saves about 0.9 ms (fv1) and 0.5 ms (forest), and shortens every shadow.
- The High preset (x1.5, 4 cascades) costs +1.1 ms (fv1) and +0.8 ms (forest).

**Method:**
- Bench build of a38f9ca, v2, uncapped, 1280x720.
- The bench runner's new BenchTune sweep (`-benchtunesweep`) runs every setting in one session, so the light, weather and camera match: 4 s settle, 8 s measured, screenshot.
- 2 sessions per spot, the second in reverse order. Shown: forest mean of both; fv1 from session 1, because session 2 froze (see notes).
- GPU ms from FrameTimingManager.
- Default here is the bench profile's Shadows 1.0: 50 m, 3 cascades; trees cast to 60 m and shrubs to 40 m (W-102), both capped by the 50 m shadow distance.
- Scripts: lanes/profiler/scripts/q33.sh (separate sessions, numbers only) and q34.sh (sweep). Sheets: shots/job33/sheet-fv1.jpg, sheet-forest.jpg. Logs and full shots: results/q33logs, q34logs, q34shots.

| setting | fv1 gpu ms (fps) | forest gpu ms (fps) | look |
|---|---|---|---|
| default (x1 = 50 m, 3 cascades, W-102 cast 60/40 m) | 13.07 (74) | 9.89 (97) | — |
| High preset: x1.5, 4 cascades | 14.20 (67) | 10.66 (87) | Same as default in these views. |
| Option 1: x0.75, 3 cascades | 12.68 (77) | 9.79 (99) | Close to default; shadows end at 38 m. |
| Option 1: x0.5, 2 cascades | 12.14 (74) | 9.42 (102) | Shadows end at 25 m; far trees unshaded. |
| Option 2: trees 30 m, shrubs 20 m | 12.61 (77) | 9.63 (100) | The forest shadow stops short of the tree line. |
| No flora shadows | 11.50 (83) | 9.30 (104) | Flat: fv1 is visibly brighter under the canopy. |
| Cheap far caster | 11.95 (81) | 9.58 (101) | **Forest: a big solid shadow blob, darker and larger than the canopy's dappled shadow.** |
| Still far shadows | 13.69 (71) | 9.90 (98) | Like default. **Slower** at fv1. |
| Cheap + still | 11.80 (81) | 9.56 (101) | As cheap. |
| Cheap + still, near 15 m | 11.47 (84) | 9.51 (101) | As cheap; the solid blob comes closer. |
| Cheap + still + option 1 x0.75 | 11.60 (83) | 9.50 (101) | As cheap. |

**For the world lane (W-107):**
1. **Cheap far caster's shape is too solid.** It draws a filled silhouette where the real tree has gaps, so far shadows get darker and blockier than near ones, and the seam at the near range is visible.
   - Suggestion: give the card or mesh alpha-tested holes from a canopy noise texture, or a lower shadow strength for the far caster.
2. **Still far shadows cost +0.6 ms at fv1.** Possibly the still path adds draws rather than replacing them. Worth checking that the still and near lists don't overlap.
3. Cached far shadows (row 3) aren't in yet. They'd remove the remaining far-caster cost.

**Notes:**
- **Two freezes, cause unknown:**
  - q34 fv1 session 2 froze for 452 s in one frame, during the "cheapstill" set. The process was alive before and after, and the system log has no sleep or display-driver event.
  - Two q33 sessions (fv1 x0.5/4 cascades, forest trees 30 m) never finished their stop in 15 min while the game kept running.
  - None of the three is tied to one setting. I'll watch for it; if it repeats I'll capture it.
- fv1 (v2 -3304,-1644) is a misty swamp forest by the village. The mist still drifts between sets, so judge fv1 looks loosely; the forest sheet is the clean comparison.
- Live use: launch the bench build (a38f9ca) with `-benchtune` (add `-island v2` etc.). The BenchTune panel shows fps, GPU and main ms. F9 hides it, F10 picks a row, F11 changes it, F12 saves a screenshot named by the settings. Rows: shadow distance, cascades, tree/shrub cast distance, flora shadows, cheap far caster, still far shadows, cached far shadows (pending), near range, still-from distance.
- Cost: 42 calls, peak 285k context (session), $5.1

## 2026-11-05 job 32: #102 island-ready frame breakdown, and the W-102 flora-shadow baseline (read only, main 7492c31)

### A) #102: the island-ready frame

**Short answer: the frame is the island bake running on the main thread.**
- The frame is 12.0 s on v2 and 14.3 s on v3. It's the same within 1% over 3 runs each, and the weather seed changes nothing.
- IslandV2System.OnUpdate is 10.4 s (v2) / 12.7 s (v3) of it. Burst jobs are only 0.5-1 s of that; the rest is managed code with no markers.
- The bake already times itself in its log (`[bake] stages ms`, `[bake] v3 generate ms`, `[island] baked in`). Those lines rank it without a deep profile.
- **Biggest wins:**
  1. Bat folk placement: 2.3 s on v2.
  2. Rivers: 2.0-2.5 s.
  3. v3 generating the whole v2 island first and throwing part of it away: 1.9 s.
  4. V3 ranges: 2.2 s.
- None of this is in profiler-owned files, so there's no self-fix. All of it belongs to the **world lane** (terrain/island/caves).

**Method:**
- Profile build, launched with `-profiler-enable` capturing from the first frame, `-fpscap 20`, `-benchstops here`.
- 3 runs each on v2 and v3. The island seed is fixed in code (`IslandV2.PickRealms`, base seed 202), so the runs vary `-weatherseed` 1-3.
- BigFrame call tree of the longest frame, plus the bake's own stopwatch lines in each run's log.
- One deep-profiling build (`-deepprofiling`). It hit the 900 s timeout inside `PickRealms` after a 4 GB capture, so its timings are distorted by deep-profiling overhead. It only showed that the unmarked time is managed noise math (`Warped -> Fbm -> Value -> H01 -> H -> Scramble`).
- Scripts: lanes/profiler/scripts/q32.sh. Logs: results/q32logs/.

**The frame (median of 3 runs, ms):**

| | v2 | v3 |
|---|---|---|
| Island-ready frame | 12 000 | 14 300 |
| IslandV2System (all) | 10 440 | 12 730 |
| of which Burst job waits (RiverBaking SampleCells about 490, RoadBaking Ground 230-400, others under 30 each) | about 900 | about 1 100 |
| `[island] baked in`: generate | 1 815 | 4 540 |
| `[island] baked in`: places (TerrainAuthoring.BuildPlaces + falls + archipelago) | 8 580 | 8 150 |
| BehaviourUpdate in the same frame (WeatherSystem about 200, CaveSystem about 160, the rest under 80 each) | about 1 090 | about 1 100 |
| Other frames: launch frame 0 / frame 1 / frame after ready | 7 200 / 1 100 / 500 | 7 200 / 1 090 / 440 |

**Ranked bake stages (ms, 3 runs: v2 range; v3 range):**

| # | stage | v2 | v3 | where | likely cause | suggested fix | lane |
|---|---|---|---|---|---|---|---|
| 1 | batfolk | 2 332-2 345 | 1 014-1 016 | `Terrain/Caves/BatFolkBaking.cs:110` (Build), called at `TerrainAuthoring.cs:229` | Managed main-thread placement. Attempt loops at :244 (huts, 60 tries) and :287 (props, 80 tries) probe the cave SDF. `Ceiling` (:326) marches up to 200 `source.CaveDistance` calls per probe. `OnPath` (:349) scans every cave segment per test. | Make the probes Burst. Put `Ceiling`/`CaveDistance` in a Burst function, or run each site's placement as a Burst job (sites are independent, so IJobParallelFor over sites). Also index segments by chamber node rather than scanning all of them. | world |
| 2 | rivers | 2 019-2 124 | 2 532-2 543 | `TerrainAuthoring.cs:159-160`: `RiverBaking.Build` twice (jittered + plain; W-76 already shares the drainage grid) | About 0.5 s is the Burst SampleCellsJob. The rest is managed tracing and smoothing, done twice. | Run the two river bakes in parallel or as jobs. Check whether the jittered map can be derived from the plain one. | world |
| 3 | generate (v2) / v2base (v3) | 1 780-1 915 | 1 864 | `Terrain/Island/IslandV2.cs:208` Generate: per-cell loop at :233 (Fbm ridge/pass noise per cell), PickRealms at :178 (2 attempts of a 96x96 Warped grid) | Managed per-cell noise (`Fbm`/`Warped`, hashes) on the main thread. | Move the :233 per-cell loop into a Burst IJobParallelFor over rows; the noise helpers are pure static math and Burst-able as they are. | world |
| 4 | v3 ranges | — | 2 227 | `Terrain/Island/IslandV3.cs:45` `V3Ranges.Build` | Managed field build. | Burst job, as row 3. | world |
| 5 | v3 throws away v2 work | — | part of 1 864 | `IslandV3.cs:33-34`: full `IslandV2.Generate`, then `gen.Island.Dispose()`. `PickRealms` is also run 3 times on v3 (`IslandV2.cs:210`, `IslandV3.cs:38`, `V3Ground.cs:54`) for the same deterministic answer. | Duplicate work. | Split Generate so v3 only builds the parts it keeps. Cache PickRealms' result (it's constant per seed). | world |
| 6 | settlements | 950-956 (roads 540-560) | 1 189-1 220 (roads 718-736) | `SettlementBaking.Build` (`TerrainAuthoring.cs:168`) | Road tracing. | Profile when next touched. | world |
| 7 | fishing | 934-954 | 747-750 | `FishingVillageBaking.Place` (`TerrainAuthoring.cs:201`) | Spot search. | Same as row 1: Burst the probes. | world |
| 8 | caves | 904-925 | 756-770 | `CaveBaking.Build` (`TerrainAuthoring.cs:196`). Its own split: shape 165, streams 194, rubble 158, plants 129, speleothems 98, fittings 97. | Spread over its steps. | Low priority. | world |
| 9 | pondmask, seafolk, anchors, turtles, icebergs, landmarks | 326-339, 246-259, 218-224, 163-208, 81-112, 74-76 | 404-418, 685-692, 138-143, 234-282, 89, 102-108 | `TerrainAuthoring.cs` stages | — | Seafolk on v3 (690) is worth a look when touched. | world |

**Notes for #102:**
- The frame was 10.0 s on 1699574 (job 27) and is 12.0 s on v2 now. The bake has grown about 2 s since then: bat folk pods (W-96 row 1, #35) and fishing villages are new stages.
- The cleanest structural fix is the one already in #102: run the bake off the main thread behind a loading screen that keeps drawing. The bake's inputs are blobs and static data, so `Generate` + `BuildPlaces` could run in a managed worker `Task`. Rows 1-5 cut the total time either way.

### B) W-102 baseline: draw counts with flora and shadows off

**Method:**
- Profile build of 7492c31, v2, uncapped, `-profilestop` capture of the stop.
- Counters averaged over every captured frame, read by lanes/profiler/scripts/Counters.cs.txt.
- Spots: fishing village 1 (`-goto -3304,-1644`, here 10 s) and the route's forest stop.
- RenderDoc isn't installed, and the Frame Debugger needs an editor attached to the player, so the counts come from the Profiler's Render counters.
- **"Draw Calls Count" and "Batches Count" aren't recorded in this build's captures.** The game's flora and props go through instanced draws, so "Instanced Batched Draw Calls" (instances drawn) and SetPass are the usable counters.
- **No counts per shadow cascade:** that needs RenderDoc or the Frame Debugger.

| spot / variant | gpu ms | fps | SetPass | shadow casters | instanced draws (instances) | tris (M) |
|---|---|---|---|---|---|---|
| fv1 plain | 13.9 | 60.6 | 208 | 340 | 22 351 | 9.05 |
| fv1 flora off | 6.8 | 95.3 | 197 | 17 | 15 208 | 6.65 |
| fv1 shadows off | 7.2 | 84.7 | 192 | 0 | 8 462 | 4.26 |
| forest plain | 9.4 | 77.3 | 187 | 417 | 17 795 | 7.68 |
| forest flora off | 6.0 | 101.5 | 176 | 19 | 18 584 | 7.94 |
| forest shadows off | 6.3 | 93.7 | 171 | 0 | 5 444 | 3.39 |

**What W-102 should move:**
- **Shadow casters:** flora is 323 of 340 casters at fv1 and 398 of 417 in the forest.
- **The shadow passes draw more than the camera.** Shadows on vs off:
  - fv1: +13.9k instances (62% of all instances) and +4.8M tris (53%).
  - Forest: +12.4k instances (69%) and +4.3M tris (56%).
- SetPass is low (171-208) and changes by only 11-16 either way, so the cost is triangles and instances, not state changes.
- **Target for W-102 with flora shadows cut to near the camera:**
  - Shadow casters fall toward about 20-60.
  - Instances fall toward the shadows-off numbers (8.5k fv1, 5.4k forest) plus the near flora.
  - GPU ms falls toward 7-8 ms at fv1 and 6-7 ms in the forest.
  - Use `-benchnoshadows` and these runs as the bounds.
- **Caveat on "flora off":** `-benchnoshader` turns off the flora shader's passes but leaves the renderers alive, so the triangle and instance counters can still count submitted flora. The forest's flora-off row (instances and tris slightly up) shows this. The shadows-off row is the clean comparison; flora off is clean only for shadow casters and gpu ms.

- Tools: Counters.cs.txt (new). BProfileTools.BuildDeep (deep-profiling build) is committed on b/bench-runner. Script: q32b.sh. Logs: results/q32logs/.
- Cost: 48 calls, peak 285k context (session), $2.08

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
