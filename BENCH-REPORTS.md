# Bench reports (kind-possum → supervisor)

Newest first.

## 2026-11-04: job 23 (Zach asked), #99 confirmation in a build: FAILS. The P-8 fix 84208ff crashes on the FIRST world switch in 7 of 7 runs; its parent be6d005 crashes on the 2nd-3rd host (3 of 3). Same native stack as before (burst.initialize.statics)
Method: Bench builds of 84208ff and be6d005, each with the NetCycle helper (job 16's repro, never committed): -netcycle 3 = host, solo, host, solo, host, solo, memory logged after each switch settles. Uncapped. Fix: 5 runs v2 + 2 runs v3; parent: 3 runs v2, interleaved.
- **Fix 84208ff:** all 7 runs end with a segfault (rc 139). Six of them (v2 runs 2-5, both v3 runs) crash inside the first GameBootstrap.Host(), before "cycle 1 hosting" is logged; v2 run 1 hosts once and crashes going back to solo. No run completes a cycle.
- **Parent be6d005:** all 3 crash too, but later: they host 3, 2 and 2 times and get back to solo 2, 1 and 2 times. That matches job 16 on f405348 (2 of 3 crashed) and 3a4acb8 (1 of 3).
- The stack in every crash, fix and parent alike: lib_burst_generated burst.initialize.statics.9af43d12..._avx2 (some also c2e4d500... or d857da5c...). This is Burst's static init on world creation, the same as job 16.
- 0 managed exceptions before the crash in every run. The bake is shared, not redone, on each switch ("shared with ServerWorld/ClientWorld ... no rebake", v2 136/B5EFF8C8).
- What 84208ff does (GameBootstrap.DisposeGameWorlds): SetActive(false) on every root of the active scene, so their OnDisable completes their jobs; then EntityManager.CompleteAllTrackedJobs() and Dispose() for each client/server/solo world. On this laptop that makes the crash come sooner, not go away. P-8 saw 5 runs / 15 re-hosts with 0 crashes, so this doesn't reproduce here. Possible differences: their build type (a Dev build?) and machine, or NetCycle calling Host()/PlaySolo() directly while their run went through the UI. Their commit says NetCycle, -island v2 -netcycle 3, so the repro should be the same.
- 84208ff is on main, so main likely has this too. I'm checking main 0ec7a8d (NetCycle build, 3 runs v2) as 23b after job 22b.
- Owner: player lane (P-8) / net bootstrap. Suggest reopening #99 (I don't change issues).
- Crash reports are under %LOCALAPPDATA%/Temp/DefaultCompany/PunchyBench/Crashes on the laptop, and the logs in results/q23/<run>/game.log. Scripts: q23.sh, q23b.sh, NetCycle.cs.txt.
- Cost: session total so far 274 calls, peak 301k context, $18.65 (job 23 about 8 calls, $0.60)

## 2026-11-04: job 20, startup hitch fix (#102) world-features cfae8be vs 4e3b63c: "[stream] world ready" v2 33.1 -> 19.1 s (-14.0 s), v3 35.1 -> 23.2 s (-11.9 s); the island-ready frame is 24.4 -> 10.8 s (v2) and 26.9 -> 14.4 s (v3); hashes as expected; the player doesn't run the v3 audit
Method: Bench builds, 3 cold starts per island per build, rotated, `-benchstops here` (no teleport), uncapped. Profile-cfae8be with the StreamTrace helper: one trace run per island, then a capture from launch at -fpscap 20 (BigFrame dump, as in job 19; 4e3b63c's capture is job 19's). One `-host` run per island (host + local client in one process). 0 exceptions in all 22 logs.
- Times, mean of 3 (s since start), 4e3b63c -> cfae8be:
  - v2: island ready 13.4 -> 13.8, BenchRoute world ready 21.6 -> 15.4, "[stream] world ready" 33.1 -> 19.1.
  - v3: island ready 16.7 -> 17.8, BenchRoute world ready 23.7 -> 19.5, "[stream] world ready" 35.1 -> 23.2 (22.8 / 22.8 / 24.0).
  - Tail after island ready: v2 19.7 -> 5.3 s, v3 18.4 -> 5.4 s.
- **Island-ready frame**, 4e3b63c (job 19) -> cfae8be, v2 (v3):
  - total 24.4 -> 10.8 s (26.9 -> 14.4)
  - bake (IslandV2System) 7.8 -> 8.6 s (11.4 -> 12.1)
  - FaunaDirector.Update 5.3 -> 1.2 s (5.3 -> 1.2); 1.0 s of that is self time
  - FloraSystem.LateUpdate 8.2 -> 0.28 s (8.3 -> 0.25)
  - CaveSystem.Update 2.5 -> under 0.1 s (1.3 -> under 0.1)
  - PresentationSystemGroup 0.2 s
  - Nothing else over 200 ms. v3 has DriftwoodSystem 0.11 s, a shader load.
- [flora] build is 182-205 ms in every run (placement 2-8, grow 162-186, support grids 16-23; 687 variants, 2.41 M vertices).
- The fix costs the bake about 0.8 s on v2 and 0.7 s on v3 (IslandV2System self 6.6 -> 7.4 s, 9.8 -> 10.5 s), so island ready comes 0.4-1.0 s later. The pregrow beside the bake likely competes with it.
- **The 5.3 s tail now:** about 1.7 s is the rest of the island-ready frame (fauna, flora, presentation). Then terrain: 22 batches. The new small first batches (3, 18, 39 chunks) don't finish faster. On v2 the 39-chunk batch runs 60 frames / 1.8 s and is only picked up at the MaxBuildFrames=60 backstop. On v3 the second full batch runs 34 frames / 0.8 s. The other 18 batches take 10-240 ms each, and streaming finishes about 1 s after that.
- **v3 audit and biome reports: not in the player.** No "[hydro audit]" or biome report lines in any player log, and nothing like them in the v3 island-ready frame. The player runs them only with -islandaudit or PUNCHY_ISLANDAUDIT=1 (IslandV2.AuditRequested). The editor's ~29 s frame means the editor has one of those set; Temp/PlayArgs.txt is read as launch args in the editor.
- **Host + local client:** one "[flora] build" per process, 190 ms v2 / 185 ms v3, with no second build for ClientWorld. FloraSystem runs on the default world, which is the client, and ServerWorld logs "shared with ClientWorld". Hosting adds nothing measurable to startup (v2 world ready 19.2 s hosting vs 19.1 solo; v3 22.3 vs 23.2).
- Hashes: v2 136/B5EFF8C8 and v3 108/3A0DC7BB in every run on both builds. The host run logs the same hash on the bake path and on the "shared with ClientWorld" path, so the palette mismatch from job 19 is gone.
- **Next levers:**
  1. The bake: 8.6 s v2 / 12.1 s v3, all main thread (world; bake cache or more W-76-style parallel stages).
  2. The first terrain batches: v2's 39-chunk batch waits out the 60-frame backstop, 1.8 s (world, cave-mouth chunks: split those or let them land later).
  3. FaunaDirector's 1.0 s self in the island-ready frame (animal; A-52's stage log names the parts).
  - At launch, before the bake: Init FMOD 1.5 s, CaveSystem.OnEnable 0.6 s.
- Scripts: q20.sh. Logs: results/q20logs/, results/q20-bigframe-v2.txt, results/q20-bigframe-v3.txt.
- Cost: session total so far 258 calls, peak 301k context, $17.07 (job 20 about 20 calls, $1.50; job 21-23 setup included)

## 2026-11-04: job 19, startup streaming tail on world-features 4e3b63c: it isn't streaming. The frame that logs "island ready" keeps the main thread for another ~16.5 s (flora build 8.2 s, FaunaDirector build 5.3 s, CaveSystem 2.5 s on v2 / 1.3 s on v3); all terrain chunks then stream in ~3 s
Method: Bench-4e3b63c, 3 cold starts per island, player left where it spawns (`-benchstops here`), uncapped. Profile-4e3b63c with a profiler-only helper (StreamTrace.cs.txt, never committed): per frame it reads TerrainStreamingSystem's batches, 2 runs per island. Then a Unity profiler capture from launch (`-profiler-enable -profiler-log-file`), v2 and v3, and the longest frames' call trees (BigFrame.cs.txt). That capture ran at `-fpscap 20` so the whole startup fits the profiler's 2000-frame window; the cap doesn't change what runs inside a frame. 0 exceptions.
- Times (mean of 3, s since start): v2 island ready 13.2, BenchRoute world ready 21.4, "[stream] world ready" 32.9 (32.3-33.5), 1804 chunks; tail 19.7 s. v3: 16.8 / 23.6 / 35.0, tail 18.2 s.
- What gates "[stream] world ready": only TerrainStreamingSystem. It logs once nothing within the view radius is left to build and no batch is in flight. Flora, ground detail, colliders and landmarks don't gate it; flora only places 4x faster while it's loading.
- **The giant frame**, v2 (v3), 24.4 s (26.9 s) of main thread:
  - Bake, before the "ready at" log: IslandV2System 7.8 s (11.4 s). It waits about 0.7 s (1.0 s) on RiverBaking:SampleCellsJob and RoadBaking:GroundJob.
  - After the log, all self time with no markers inside: FaunaDirector.Update 5.3 s (5.3 s), in Build() on its first sight of the baked TerrainSource; CaveSystem.Update 2.5 s (1.3 s); FloraSystem.LateUpdate 8.2 s (8.3 s), in Build(): FloraGenerator.Grow for every species variant, the support grids and FloraPlacement.Build; PresentationSystemGroup 0.2 s.
  - Cross-check: BenchRoute's coroutine runs after Update, so its "world ready" (21.4 s) = island ready + fauna + caves. Island ready + 16.4 s of frame + ~3 s of streaming = "[stream] world ready".
- **Streaming itself** (StreamTrace, after the giant frame): 21 batches, 1804 chunks, 86 chunks per batch. The first full batch (96 chunks nearest the player) takes 1.6 s and 53 frames. The other 19 batches take 10-100 ms each, 1-6 frames. About 3.0 s in all, and a finished batch waits for the next frame in only 14 frames.
- **Levers, ranked:**
  1. **Flora Build, 8.2 s (world lane, FloraSystem / FloraGenerator).** Grow the species models in parallel jobs, or cache the grown models (they depend on the palette, not the island). Moving them off the main thread while the bake runs hides them entirely. Estimate: 6-8 s.
  2. **FaunaDirector.Build, ~4.7 s self (animal lane).** Species blobs (CreatureBlobBuilder per species), the capsule and carry tables. The resident and placement logs in the frame (Mantrap, colossi, crossings, FolkJobs) may belong to other owners; add markers to split it. Same treatment as flora: parallelize, start it before the bake, or cache. Estimate: 3-4.5 s.
  3. **CaveSystem.Update first frame, 2.5 s v2 / 1.3 s v3 (world lane), plus the first terrain batch's 1.6 s (world lane, cave-mouth chunks; MaxBuildFrames 60).** Mesh caves in jobs or across frames, and split the first terrain batch so the nearest chunks land first. Estimate: 2-3 s on v2, ~1.5 s on v3.
  - None of these builds runs in parallel with anything today. Just moving one to another frame saves nothing unless it runs on workers, overlaps the bake, or comes from a cache. Workers are mostly idle through all 24 s.
  - The bake itself (7.8 / 11.4 s) is the next item after these. That's W-76's work and the bake cache.
- **Jobs 17/18 caveat:** their "[stream] world ready" numbers include a restream. The bench's town stop teleports the player 10 s after BenchRoute's world ready, before streaming had finished. Measured on 4e3b63c: v2 33.3 s with the town stop vs 32.9 s left in place; v3 36.1 vs 35.0 s. So W-75's 1.9 s boost saving from job 18 is the boost-on vs boost-off difference with the teleport in both runs; it isn't re-measured here.
- **v3 hash (3A0DC7BB player vs 4D2643D7 editor probe):** the player's line is IslandV2.LandmarkHash on the bake path (IslandV2.cs:533). It hashes each landmark as "type name, x, z, scale", sorted, FNV-1a, and takes the type name from the world's BiomePalette.Landmarks; an index past the palette's end hashes as the number. So the same instances hash differently with a different palette. Proof: in job 16, one player process logged the same v2 bake as 136/B5EFF8C8 on the bake path and 136/728F95DF on the "shared with" path (other worlds' palettes). Laptop editor play mode on f405348 logged 136/B5EFF8C8 too, the same as the player bake path. The 4D2643D7 probe is the world lane's untracked Temp/w43_hash.cs on the desktop, which I can't see; it most likely builds its names from a different type list (LandmarkTypes with looks and the code-loaded villages) than the baked palette. Fix suggestion (world): hash the type's name from the same list the bake used (LandmarkTypes), or hash names only for types the palette has. Then every path agrees and a mismatch means a real difference.
- Tracer note: StreamTrace's marker recorders filled their 120 slots with engine markers, so per-system ms came from the profiler capture instead.
- Scripts: q19.sh, q19b.sh (uncapped capture; the 2000-frame window missed the giant frame), q19c.sh, StreamTrace.cs.txt, BigFrame.cs.txt. Results: results/q19logs/, q19-bigframe-v2.txt, q19-bigframe-v3.txt.
- Cost: session total so far 206 calls, peak 301k context, $13.50 (job 19 about 39 calls, $2.35)

## 2026-11-04: job 17, W-76 parallel island bake (world-features b262269 vs its parent 096ec00): v2 bake 9.9 -> 7.8 s and island ready 15.3 -> 13.1 s; v3 bake 12.8 -> 11.2 s, ready 18.2 -> 16.5 s; hashes identical; startup streaming unchanged
Method: Bench builds (player, laptop), 3 cold starts per island per build, order rotated, uncapped, `-benchstops town`. Each log has the [bake] stage line, "baked in" with the landmark hash, island ready, BenchRoute world ready and W-75's "[stream] world ready". 0 exceptions in all 12 logs.
- v2, mean of 3, before -> after (ms): total bake 9925 -> 7771 (-2.15 s, -22 %); places 8773 -> 6611; generate 1151 -> 1157. Rivers 2287 -> 1858. The four placements (the old "batfolk" stage) 3717 -> 1792 (fishing 941, turtles 194, seafolk 246, batfolk 609). Landmarks, settlements, caves, anchors, pondmask and icebergs are unchanged (within 40 ms).
- v3, mean of 3 (ms): total bake 12825 -> 11221 (-1.6 s, -13 %); generate 3743 -> 3787; rivers 3123 -> 2536; the four placements 3058 -> 1983 (fishing 771, turtles 236, seafolk 724, batfolk 252); the rest unchanged.
- Island ready: v2 15.27 -> 13.10 s; v3 18.17 -> 16.53 s. BenchRoute world ready: v2 23.30 -> 21.17 s, v3 24.97 -> 23.37 s. "[stream] world ready" (W-75 boost on): v2 35.33 -> 33.27 s, v3 37.43 -> 35.67 s.
- Startup streaming (island ready -> "[stream] world ready") is unchanged, as expected: v2 20.1 s before and 20.2 s after; v3 19.3 s and 19.1 s. The whole startup gain comes from the bake. W-75's load boost already saves about 1.9 s there (job 18). The next bigger lever is the ~20 s streaming tail or the bake cache.
- Hashes are identical before and after on every run: v2 136 landmarks, hash B5EFF8C8; v3 108 landmarks, hash 3A0DC7BB. The player prints only the landmark hash, not W-76's cave, ground and river hashes; those match in W-76's editor check.
- The laptop player's stages take about 1.5-2x W-76's editor numbers, but the saving is similar (v2 -2.15 s here vs -1.4 s in the editor). With the -islandaudit gate (W-74), v3 is ready at 16.5 s instead of 55 s on main f405348.
- Worst frame during the bake isn't measured: a Bench run measures nothing until the world is ready. The bake runs in one frame, so that frame is at least the "baked in" time (7.8 s v2, 11.2 s v3). W-76's editor check put it at 7.4 s.
- Scripts: q17.sh. Logs: results/q17logs/.
- Cost: session total so far 167 calls, peak 301k context, $11.15 (job 17 about 14 calls, $0.60)

## 2026-11-04: job 18, W-75 (world-features 096ec00) and A-51 (animal-behaviours b527dc1) vs main 9d34275: A-51 is a real win where creatures are (hut cave 96.8 -> 125.8 fps, median 10.07 -> 7.83 ms); W-75 is frame-neutral and its load boost brings "[stream] world ready" 37.3 -> 35.5 s
Method: Bench builds of 9d34275, 096ec00, b527dc1, plus Bench-f405348n (A-51's base, from job 16). Island v2, uncapped, 3 rounds with the order rotated. Each round runs the q13 route (town, townwalk, cave, caveapproach, glide) and the hut cave floor (`-goto 530,45,958`). W-75 also ran 3 times with PUNCHY_LOADBOOST=0. Dev profiles at townwalk and the hut cave for the three new SHAs. 0 exceptions in every log.
- Mean of 3 (avg fps / 1% low / median ms / worst ms), main -> W-75 -> A-51 (A-51's base f405348 in brackets):
  - hut cave: 96.8/58.3/10.07/35.6 -> 96.1/56.7/10.07/35.8 -> **125.8/76.1/7.83/32.1** [96.1/57.5/10.10/33.4]
  - cave: 116.9/70.4/8.37/28.5 -> 120.8/72.7/8.07/26.7 -> **135.5/87.7/7.30/26.9** [119.1/70.4/8.20/27.5]
  - town: 86.0/53.5/11.60/27.2 -> 86.1/50.4/11.60/25.9 -> 88.4/56.0/11.37/21.8 [85.7/53.4/11.67/23.3]
  - townwalk: 78.4/37.5/12.30/33.9 -> 79.5/38.7/12.17/34.4 -> 80.5/44.6/12.17/27.4 [79.2/38.5/12.20/34.0]
  - caveapproach: 62.1/28.8/15.73/46.8 -> 60.9/27.9/15.97/47.7 -> 63.7/31.5/15.47/41.4 [61.8/28.0/15.77/53.3]
  - glide: 89.4/37.7/10.63/37.6 -> 89.7/37.7/10.53/36.0 -> 88.6/40.8/10.53/31.8 [89.3/36.5/10.67/37.9]
- A-51, main-thread self ms (dev profile, frame mean). Hut cave: CreatureRenderSystem:PoseJob 1.63 -> off the main thread; CreatureRenderSystem 0.58 -> 0.23; Brain.Tick + Brain.Advance 0.64 -> 0.02; ReachJob 0.30 -> off the main thread. Frame 12.46 -> 9.30 ms. Townwalk: PoseJob 0.34 and the debug HUD (DebugControls.OnGUI 0.25 + GUIStyle.GetDimensions 0.22, so about 0.45 ms, close to the 0.5 ms estimate) are gone; WaitForJobGroupID 0.29 -> 0.12. Frame 12.58 -> 12.38 ms. On the worst frames A-51 is 5-12 ms better at townwalk, caveapproach and glide.
- W-75: frame-neutral everywhere (every stop within noise of main). TerrainStreamingSystem 1.00 vs 0.99 ms and FloraSystem.LateUpdate 0.64 vs 0.65 ms at townwalk. In the hut cave Brain.Tick + Advance read 0.34 vs 0.64 ms on main; only one capture, so treat that as noise until it's measured again.
- World ready (3 runs each): BenchRoute's "world ready" is about 23.5 s on all four builds (23.2-23.8 s), and the island is ready at 15.2-15.6 s, so W-75 doesn't change either. W-75's own "[stream] world ready" line comes later (it waits for the full streaming radius): 35.4-35.8 s with the boost on and 37.2-37.5 s with PUNCHY_LOADBOOST=0. **The boost saves about 1.9 s (37.3 -> 35.5 s)**, which is 9 % of the 21.9 s from island ready to world fully streamed. Only W-75 logs that line, so there's no main number to compare it with.
- Scripts: q18.sh. Results: results/q18.csv, q18-table.txt, q18logs/, prof18-*.
- Cost: session total so far 153 calls, peak 301k context, $10.55 (jobs 12-18 and Zach's questions; per-job split isn't recorded)

## 2026-11-04: item 16, memory recheck on main f405348: the W-73 fix holds (editor Total Used flat over 10 plays on v2 and v3, no Persistent leak at shutdown); separately, an intermittent player crash on the 2nd host in one session, also on main before W-73
Builds: Bench-f405348n and Bench-3a4acb8n, each with a profiler-only helper (lanes/profiler/scripts/NetCycle.cs.txt, never committed: -netcycle N hosts and goes back to solo N times, logging memory). Editor: batchmode on f405348 with MemCheck (-fasttiming -nosnap). Play 1 runs with Burst safety checks, the jobs debugger and leak detection off; leak detection is Enabled for the 10 cycles; the editor's settings were put back at exit (they were: safety checks on, jobs debugger on, leak detection Enabled).
- Editor v2, Total Used (MB): idle 2174, play 1 after the bake 3129, after exit 2291, cycles 1-10: 2131 2131 2131 2132 2132 2132 2132 2000 2000 2000. **Flat** (job 14 on 5d0956a: +22 MB every cycle). The shutdown check has no "Persistent allocates" line; only the old "2 preview scenes not closed".
- Editor v3, Total Used (MB): idle 2315, play 1 3129 -> 2672, after exit 2092, cycles 2092 2092 2092 1910 1910 1911 1911 1911 1911 1911. Flat, no Persistent leak.
- Timing caveats: play 1's bake took 159 s (v2) / 107 s (v3) because switching safety checks off makes Burst recompile on first use, so play 1 isn't a bake time. Later plays: editor v2 ready at ~24 s; v3 at ~67 s, with "[island] reports" 41-44 s. The v3 audit is still on in f405348 (no -islandaudit gate yet; W-74 isn't on main). Player v3: reports 38.0 s, ready at 55.2 s.
- Player, host/solo cycles (Total Used MB: solo start / then after each switch): f405348 v3 427 / 504 444 507 449 499 449; v2 416 / 493 428 496 432 498 431. Flat after the first host (+12-20 MB that stays). Players never rebaked on host or solo ("shared ... from this process's bake (no rebake)"), so the editor leak never reached players this way. 0 exceptions.
- **Crash:** the v2 player crashes (segfault, rc 139) going into the 2nd host after one host -> solo. The native stack starts in lib_burst_generated burst.initialize.statics (Burst static init on world creation). f405348 v2: 2 of 3 runs crashed; v3: 0 of 1. Main before W-73 (3a4acb8) v2: 1 of 3 crashed with the same stack. So it is older than W-73 and intermittent. Crash reports: %LOCALAPPDATA%/Temp/DefaultCompany/PunchyBench/Crashes on the laptop. Repro: Bench build + NetCycle, `-island v2 -netcycle 3`. Owner: shared/net bootstrap (GameBootstrap.Host after PlaySolo). In the game this is Host -> Stop hosting -> Host again from the Multiplayer tab.
- Scripts: q16.sh, q16b.sh (repro), q16c.sh (pre-W-73), MemCheck.cs.txt (-fasttiming, -nosnap), NetCycle.cs.txt.
- Cost: 36 calls, peak 289k context, $3.01 (includes Zach's bake/streaming/creature questions)

## 2026-11-03: item 15, Burst and jobs audit on main 3a4acb8: every runtime job is already Burst; the time goes to managed main-thread code and same-frame job syncs, with workers 7-16% busy
Method: Profile-3a4acb8 (dev build, so no safety checks), island v2, uncapped, one capture per q13 stop (town, townwalk, cave, caveapproach, glide, hut cave floor, FishWharf steps), 0 exceptions. I read the code with git grep on 3a4acb8 and didn't open the Burst Inspector (it's an editor window). The cave stop settled in 10.8 s, caves built.
- Main vs workers (mean ms per frame; workers = job time summed over the 7 worker threads; main busy = main-thread self time without waits):
  - town: frame 11.6, main busy 7.4 + waits 2.3, workers 8.5 (10.5% of 7 threads)
  - townwalk: frame 12.5, main 7.9 + 2.7, workers 10.7 (12.2%)
  - cave: frame 9.7, main 5.9 + 2.0, workers 4.5 (6.7%)
  - caveapproach: frame 16.2, main 11.1 + 3.1, workers 18.1 (15.9%)
  - glide: frame 11.8, main 7.9 + 1.9, workers 12.7 (15.4%)
  - hut cave: frame 12.1, main 8.5 + 1.8, workers 5.9 (7.0%)
  - wharf: frame 10.4, main 7.7 + 0.8, workers 5.3 (7.2%)
  The main thread is the limit; the workers have room for far more. WaitForJobGroupID (main waiting on jobs) is about 1.3 ms a frame at townwalk.
- Top 10 main-thread costs (mean self ms over the 7 stops; kind; Burst/jobs):
  1. TerrainStreamingSystem 0.65: SystemBase, managed. Its mesh build job is Burst and parallel; the mesh apply and entity work run on the main thread.
  2. CreatureRenderSystem 0.52: SystemBase, managed per-creature loops. CullJob is Burst but runs with .Run() on the main thread (CreatureRenderSystem.cs:547). PoseJob is Burst and parallel but completed in the same frame (:724), so 0.48 ms more of it runs on the main thread.
  3. FloraSystem.LateUpdate 0.45: MonoBehaviour, managed; PlaceJob is Burst and parallel.
  4. ThirdPersonCamera.LateUpdate 0.43: MonoBehaviour; ArmProbe is Burst, run with .Run() on the main thread.
  5. AmbienceSystem.Update 0.39: MonoBehaviour, managed; the heat job is Burst but scheduled and completed in the same frame.
  6. CaveSystem.Update 0.32 (0.75 in the hut cave): MonoBehaviour bookkeeping; the field and mesh jobs are Burst and parallel.
  7. WeatherSystem.Update 0.24: MonoBehaviour, managed; WeatherField MapJob is Burst.
  8. DebugControls.OnGUI 0.23, plus IMGUI GUIStyle.GetDimensions/GetMeshInfo 0.27: managed IMGUI, no jobs. The HUD is on by default (ShowReadout = true).
  9. CreatureAudioSystem 0.18: SystemBase, managed, no jobs.
  10. AmbientParticleSystem.Update 0.17: MonoBehaviour; its Burst job is .Schedule().Complete() in the same call.
  Next: TraceSystem 0.15 (SystemBase; GroundJob is Burst, .Run()), Mantrap.Update 0.15, PlayerMovementSystem 0.11 (ISystem, Burst).
- Grep results:
  - All 45 runtime IJob structs have [BurstCompile]; the only one without is the editor-only TerrainAuthoringEditor SampleBiomeJob.
  - 15 ISystems: OnUpdate is [BurstCompile] on all but ConnectionLogSystem and NetPlayerVisibilitySystem (net, cheap).
  - 16 managed SystemBase systems, among them CreatureRender, CreatureAudio, TerrainStreaming, Trace, InteractionInput, CreatureSnapshot/Wake/Breath/Burrow, FolkTalk, PlayerInput, Iceberg, SaltTower, SunkenTemple, SkyEvents.
  - Per-frame same-frame syncs: InteractionInputSystem.cs:100 (Dependency.Complete right after ReachJob), CreatureRenderSystem :547 and :724, ThirdPersonCamera.cs:120 (.Run), TraceSystem.cs:131 (.Run), AmbientParticleSystem.cs:233, CritterSystem.cs:212, GroundDetailSystem.cs:237, AmbienceSystem heat job, FloraSystem Placing.Complete.
  - No Entities.ForEach or WithoutBurst anywhere.
- Editor Burst settings: none are saved in EditorPrefs, so the editor runs at Unity's defaults: Burst compilation on, safety checks on, jobs debugger on, leak detection on. For editor profiling, turn off safety checks, the jobs debugger and leak detection (Jobs menu / Preferences). Players aren't affected: no BurstAotSettings in ProjectSettings means the default AOT (Burst on, no safety checks). Not changed.
- **5 biggest wins (estimates, main-thread ms per frame):**
  1. CreatureRenderSystem: schedule CullJob and PoseJob early and complete them late in presentation instead of .Run() + same-frame Complete, and move the managed per-creature loops (colonies, swarms, critters, sky birds, cave fish) into a Burst job. About 0.6-0.9 ms. Owner: animal.
  2. InteractionInputSystem: Complete() right after scheduling ReachJob also forces the brain job to finish on the main thread (Main-thread ReachJob 0.16 + Brain.Tick 0.16 + Brain.Advance 0.11, up to 0.35 ms of ReachJob in the hut cave). Using last frame's result would remove the stall. About 0.3-0.5 ms. Owner: animal.
  3. MonoBehaviour Burst jobs that are scheduled and completed in the same call (Ambience heat, AmbientParticle, Critter, GroundDetail, Flora placing): schedule in Update, complete in LateUpdate or the next frame. About 0.3-0.5 ms. Owner: world.
  4. Debug HUD: about 0.5 ms of IMGUI every frame with the readout on, which it is by default. Off by default outside dev builds, or lay it out a few times a second instead of every GUI event. About 0.4-0.5 ms in a release build. Owner: shared.
  5. TerrainStreamingSystem: 0.65 ms managed main thread on every stop (0.97 at townwalk). Write meshes through Mesh.MeshDataArray in the Burst build job and batch the entity creation. About 0.3-0.5 ms. Owner: world.
  Together about 2-3 ms of an 8-11 ms main thread. GPU time sits around 9 ms at town and townwalk, so from there the GPU becomes the limit; caves (GPU about 2.5 ms) gain the full amount.
- Scripts: lanes/profiler/scripts/q15.sh. Profiles: results/prof15-*.txt.
- Cost: 20 calls, peak 227k context, $1.44

## 2026-11-03: item 14, memory check on main 5d0956a: each play-mode cycle leaks about 22 MB of native memory (12 island-bake blobs never disposed); the editor is mostly the editor; player 1.0-1.15 GB
Runner change (asked after item 13): the biggest-cave stop now settles until CaveSystem.Settled, 30 s at most, and logs the time (b/bench-runner 3f76120). On 5d0956a it took 11.1 s (v2) and 10.0 s (v3), caves built both times.
Method: batchmode editor (no window, so less RAM than an interactive editor) on 5d0956a, island v2 (default), with a temporary editor script (lanes/profiler/scripts/MemCheck.cs.txt, never committed): idle with the scene open, play 1 after the bake, after exit (GC + UnloadUnusedAssets, snapshot), 10 more enter/exit cycles, then the same clean-up and a second snapshot. Snapshots (about 1.3 GB each, open in the Memory Profiler package and Compare): Punchy-wt/Tools/bench/results/q14/after-bake.snap and after-10-plays.snap. Process working set read 0 under Unity's Mono, so RAM below is Unity's System Used Memory.
- Editor RAM (System Used / Total Used, MB): idle 2735 / 1802; play 1 after the bake 3149 / 2173; after exit 2925 / 1730; after 10 more plays **3241 / 1972**.
- **Leak, about 22 MB per play** (Total Used 1730 -> 1773 -> 1795 ... 1972, a straight line; System Used +32 MB per play). Managed heap (919-921 MB) and Unity objects (103 MB) stay flat. Unity's shutdown check: "Persistent allocates 132 individual allocations" after 11 plays = 12 per play. A 2-cycle run with leak stack traces (results/q14b/editor.log) names all 12, every one a BlobAssetReference from the island bake that is never disposed when the world goes: IslandV2.Generate HeightmapBlob + BiomeMapBlob (IslandV2.cs:411), TerrainSource.BuildPondMask HeightmapBlob (TerrainSource.cs:571), RiverBaking.Build RiverMapBlob, LandmarkBaking.BuildBlob, SettlementBaking.BuildBlob, AnchorBaking.BuildBlob, CaveBaking.BuildBlob, BatFolkBaking.Build, IcebergBaking.Build, GrandFalls.Build (IslandV2.cs:448), Archipelago.Build (IslandV2.cs:451). Owner: world (island bake). Editor play cycles for sure; a player build would leak the same only where it bakes again in the same process (host/join, scene reload). Also small: +1 DefaultWorldInitializationProxy GameObject and +6 Texture2D per play (0.05 MB in all).
- Is the bake a big part? Only while playing: play 1 adds 371 MB Total Used over idle, mostly meshes (5636 meshes, 286 MB: flora/prop LODs, the biggest CanopyTree LOD0 4.2 MB) and +161 MB gfx. Exiting frees all of it except the leak above. In edit mode the editor is the editor: GC heap 1041 MB (MonoScripts, editor managed state), Unity objects 103 MB.
- Top 10 types after the bake, edit mode (MB): Texture2D 42.7 (251), Shader 20.9 (167), SceneVisibilityState 9.0, MonoScript 8.5 (10662), MonoManager 4.9, Object 4.1, AssetBundle 3.6, Font 2.5, Texture3D 2.0 (CloudNoise), ComputeShader 1.5. Top objects: GizmoIconAtlas_pix32 21.3, SceneVisibilityState 9.0, MonoManager 4.9, CloudNoise 2.0, AudioManager 1.4, SpeedTree8/9 shaders 1.0 each, Default-Skybox-Cubemap 1.0.
- Top 10 types in play after the bake: Mesh 285.6 (5636), Texture2D 44.9, Shader 20.9, AudioClip 12.2 (144), SceneVisibilityState 9.0, MonoScript 8.5, MonoManager 4.9, Object 4.1, AssetBundle 3.6, Font 2.5.
- Player build (Bench-5d0956a, uncapped, process memory at the end of each stop): v2 town 1102 MB, cave 1152 MB; v3 town 1040 MB, cave 1079 MB. v3 bakes much longer: [island] reports 18045 ms, ready at 35.0 s vs 15.2 s on v2 (world ready 41.7 vs 23.5 s); v3 is still behind its flag. 0 exceptions in the player runs. The editor log's 5 "Exception" lines are shutdown binding messages, not exceptions.
- Scripts: q14.sh, q14b.sh, MemCheck.cs.txt.
- Cost: 19 calls, peak 195k context, $1.42

## 2026-11-03: item 13, merged main 913d1b2 vs 27bf8c1: hut-cave gap closed (camera 0.18 ms, trace 0.04 ms), other stops within noise; the biggest-cave stop catches the cave mesh bake, more of it on 913d1b2
Builds: Bench/Profile 913d1b2 (new), 27bf8c1 (from job 12). Island v2, uncapped, 1280x720, Medium, 2 rounds with the order flipped, 0 exceptions, 0 NaN bounds, no dialogs, same end x,z on both builds.
- Bench (avg fps / 1% low / CPU ms), 27bf8c1 | 913d1b2: town 85.8/57.0/11.7 | 86.0/58.5/11.6; townwalk 80.2/37.5/12.4 | 79.6/40.1/12.6; caveapproach 61.5/27.4/16.2 | 61.2/27.5/16.4; glide 89.8/39.5/11.1 | 89.3/38.5/11.1; FishWharf steps 108.7/63.4/9.2 | 109.7/67.7/9.1; hut cave floor 90.8/52.9/11.0 | **94.3/56.1/10.6**; biggest cave 119.7/41.7/8.4 | **105.8/37.3/9.4**.
- Named markers on 913d1b2 (mean ms): ThirdPersonCamera.LateUpdate hut cave 0.18 (world-features in job 12: 1.66; main 0.19), townwalk 0.41; TraceSystem hut cave 0.04 (job 12: 0.42). PlayerMovementSystem wharf 0.038, hut cave 0.389, biggest cave 0.33 (27bf8c1 0.33). ReachJob (main + workers) townwalk 0.14, wharf 0.43, hut cave 0.67. The hut-cave profile mean is 13.02 -> 11.61 ms.
- Biggest cave: the view, cave chunks (180, 57 meshed) and triangles (393k) are the same on both builds, but 913d1b2 was slower in both rounds (109.7/102.0 vs 122.4/117.0). In the profile, CaveSystem Column/Field/MeshJob run during the measured window on 913d1b2 (2.8 ms worker time per frame, 6 frames > 33 ms, up to 219 ms) and not on 27bf8c1. With 30 s extra wait (-benchwait 30) both builds still mesh there (913d1b2 4.2, 27bf8c1 3.1 ms per frame), so the stop catches part of the cave bake on both builds, not a steady cost. CaveSystem.cs didn't change between them; world ready is about 0.6 s later on 913d1b2 (23.2 vs 22.6 s). Watch, not a regression yet. Owner: world (caves). A longer settle at the cave stop would take the bake out of the measurement.
- Scripts: lanes/profiler/scripts/q13.sh, q13b.sh (cave profiles), q13c.sh (30 s wait). Shots: results/shots-q13/ (every stop on both builds).
- Cost: 24 calls, peak 154k context, $1.22

## 2026-11-03: item 12, box collision cost (#79): the collision itself costs nothing measurable; world-features is ~0.6 ms slower in the hut cave only because it lacks main's A-45 camera probe
Builds: Bench and Profile for 27bf8c1 (main), b28ee7b (52b4af5's parent) and 52b4af5. world-features isn't based on 27bf8c1 (merge base c39bc42), so b28ee7b -> 52b4af5 isolates #79. Island v2, laptop, 1280x720, Medium, **uncapped**, 2 rounds with the order flipped, 0 exceptions, 0 NaN bounds, no dialogs, every stop ended at the same x,z on all builds.
- Bench (avg fps / 1% low / CPU ms, mean of 2 rounds), main | b28ee7b | 52b4af5:
  - town 86.5/59.2/11.6 | 84.6/51.0/11.8 | 85.1/51.3/11.8
  - townwalk 78.6/31.9/12.7 | 79.1/39.2/12.7 | 79.5/40.4/12.6
  - cave (biggest) 111.8/41.3/8.9 | 107.7/37.6/9.3 | 107.3/39.1/9.4
  - caveapproach 61.5/29.2/16.3 | 61.7/29.5/16.2 | 61.5/28.9/16.3
  - glide 88.0/36.2/11.3 | 88.7/37.4/11.3 | 89.0/40.7/11.2
  - wharf (-place prop:FishWharf, standing by its steps) 109.0/63.8/9.2 | 113.7/71.4/8.8 | 113.2/69.3/8.8
  - hut cave floor (-goto 530,45,958, batfolk cave 26, surface hidden 1.00) 93.0/53.5/10.8 | 87.1/54.9/11.5 | 87.6/52.0/11.4
- Named markers, mean ms per frame (main | b28ee7b | 52b4af5):
  - PlayerMovementSystem: wharf 0.035 | 0.034 | 0.038; hut cave 0.397 | 0.405 | 0.389. Not listed at townwalk, where the route carries the player.
  - ReachJob (main + workers): townwalk 0.14 | 0.14 | 0.15; wharf 0.42 | 0.42 | 0.42; hut cave 0.78 | 0.73 | 0.76.
  - ThirdPersonCamera.LateUpdate: townwalk 0.40 | 0.42 | 0.40; hut cave **0.19 | 1.67 | 1.66**. Not listed at the wharf.
- Collision (b28ee7b -> 52b4af5): no marker or stop moved beyond noise; profile frame means 12.60 -> 12.45 (townwalk), 10.39 -> 10.39 (wharf), 13.05 -> 12.96 (hut cave).
- main -> 52b4af5: the hut cave loses 5.4 fps (CPU +0.6 ms). The cause is ThirdPersonCamera +1.47 ms and TraceSystem +0.26 ms, both already in b28ee7b. main has A-45 (3abc2c4: Burst arm probe, trace check order) and world-features doesn't, so the gap closes when world-features takes main. Town, townwalk and glide on main vs world-features are within noise. The wharf is +4 fps on world-features.
- Top main thread (52b4af5, mean self ms): townwalk TerrainStreamingSystem 0.97, Profiler.FlushMemoryCounters 0.90 (dev builds only), FloraSystem.LateUpdate 0.63, CreatureRenderSystem 0.41, ThirdPersonCamera 0.40. Wharf: TerrainStreaming 0.90, FloraSystem.LateUpdate 0.55, CreatureRenderSystem 0.44, AmbienceSystem 0.37, AmbientParticleSystem 0.30. Hut cave: ThirdPersonCamera 1.66, CaveSystem.Update 0.75, CreatureRenderSystem 0.56, TraceSystem 0.42, PlayerMovement 0.39.
- Caveats: the wharf is a standing measurement next to the deck, not a walk across it. The "-goto 530,958" in the request lands on the surface above the cave, so I re-ran the spot as x,y,z (surface numbers, avg about 69-74 fps, are left out). Interim: re-run when W-67b lands (scripts/q12.sh + q12b.sh).
- Shots: Punchy-wt/Tools/bench/results/shots-q12/ (52b4af5 wharf, town and hut cave; hut cave on all 3 builds).
- Cost: 43 calls, peak 120k context, $2.29

## 2026-11-03: item 11, slow caves on main 762c16a: standing still the cave beats open ground; walking along cave walls the camera probe eats up to 5 ms; the cost is the v2 cave field, not the mesh
Builds: Bench-762c16a and Profile-762c16a. The comparison is v0.11.0 (4eea658, v1 island only). Laptop, 1280x720, Medium settings, 0 exceptions, 0 NaN bounds.

**Bench, cap 60, ABBA (2 rounds)**: avg fps / 1% low / cpu ms / gpu ms

| | cave | caveapproach (open ground) |
|---|---|---|
| v0.11.0 | 59.7 / 56.2 / 16.8 / 3.0 | 58.3 / 34.9 / 17.1 / 7.8 |
| main v1 | 59.8 / 58.1 / 16.7 / 3.2 | 59.3 / 41.8 / 16.9 / 7.7 |
| main v2 | 59.6 / 47.1 / 16.8 / 3.4 | 58.0 / 33.4 / 17.2 / 12.1 |

- **Pass target, standing at the bench stop: PASS.** On both islands the cave is better than open ground on avg and on 1% low, and uses a quarter of the GPU.
- The v2 cave's 1% low (47 vs 58 on v1) comes from cave chunk streaming spikes. In those frames FieldJob takes 143 ms and ColumnJob 57 ms on workers, while main idles 16 ms waiting for them. This is item 15's per-chunk cost.

**Walking in the big cave (`-gocave #1`, 60 m legs east/north/west/south, cap 60, `-perflog`): this is the slow cave**

| leg | v2 main ms | v2 frame max | v2 camera probe | v1 main ms | v1 frame max | v1 camera probe |
|---|---|---|---|---|---|---|
| start | 7.8 | 22.6 | 0.86 ms | 5.0 | 17.8 | 0.41 ms |
| east | **14.1** | 23.7 | **5.0 ms** | 6.8 | 17.3 | 1.8 ms |
| north | 9.0 | 17.2 | 1.6 ms | 5.5 | 17.2 | 0.58 ms |
| west | 9.7 | 24.5 | 1.3 ms | 5.7 | 17.5 | 0.61 ms |
| south | 9.0 | 25.7 | 0.94 ms | 5.8 | 17.6 | 0.18 ms |

- The walks aim straight and end against walls, with push-out sliding the player along them. That's where the probe climbs.
- The CaveArm sphere-trace takes small steps when it's near rock (5 cm minimum, up to 160 steps), so a camera close to the wall pays the most.
- On v2, east hits 14 ms of the 16.7 ms frame on the main thread alone. With the 22-26 ms frame max, a weaker moment drops frames.
- v1 never goes past 17.8 ms.

**Collision detection (player and camera)**
- Each sample of the cave field (IsRock / CaveDistance) costs **4.1 µs on v2 against 1.5-1.6 µs on v1, 2.7x as much**. Both run Burst.
- `CaveSampling.Distance` walks every segment in its grid cell, plus FloorShape, StreamBed and Speleothems. v2's bigger, denser caves put more segments in each cell.
- Every collision user pays this per sample:
  - camera CaveArm: 0.4-5 ms, above
  - player PushOutOfSolids plus SolidNormal: up to about 48 samples a frame. `-nopushout` saves 0.45 ms main in the v2 big cave.
  - creatures: ReachJob 0.24 ms main plus 0.32 ms workers, and the brain's UnderRock
  - cave meshing: FieldJob and ColumnJob
- Main-thread profile in the cave, v2 against v1: ThirdPersonCamera.LateUpdate 1.38 vs 0.42 ms, PlayerMovement 0.33 vs 0.17.
- v0.11.0 to main v1 (old camera and collision to new): camera −0.66, PlayerMovement +0.14, ReachJob +0.09 ms. The new sphere-trace is cheaper than v0.11's probe on v1. v2's field cost eats that gain.

**Is the bigger cave mesh the problem? No.**
- v2 draws 201-240 k cave tris against 59-82 k on v1, and keeps 20-26 chunks in view against 8-10.
- `-nocavewalls` (walls not drawn) changes frame, main and GPU by about 0 on both islands. `-oldcave` changes nothing either.
- Cave GPU is 2.1-3.4 ms total on either island. The draws are cheap.
- What grew is the **field** under the mesh: per-sample cost (collision, camera, creatures) and chunk build spikes (1% low).

**Top 5 inside a cave, main thread** (dev profile at the cave stop, mean self ms, v2 / v1)
1. ThirdPersonCamera.LateUpdate (CaveArm): 1.38 / 0.42. **World** (camera). Up to 5 ms while walking (above).
2. CaveSystem.Update: 0.58 / 0.32. **World** (caves).
3. CreatureBrain main-thread part: 0.51 / <0.15. **Animal**.
4. AmbienceSystem.Update: 0.44 / 0.15. **World**.
5. CreatureRenderSystem: 0.34 / 0.15. **Animal**. This was 10.5 ms in item 6 and 2.0 ms on v0.11; now fixed.

Next after these: PlayerMovement 0.33 / 0.17 (world, push-out), WeatherSystem.Update 0.29 / 0.20 (world), ReachJob 0.24 (animal), CreatureAudio 0.22 (animal), Mantrap 0.17 / 0.17 (world).

Workers (v2): BrainJob 1.57 (animal), WeatherField MapJob 0.50 (world), Brain.Advance 0.45 (animal), cave ColumnJob 0.39 and FieldJob 0.26 (world), ReachJob 0.32 (animal).

**GPU inside a cave**
- FrameTime.GPU is 2.98 ms on v2 and 3.45 ms on v1.
- `-benchgpupasses` listed 237 GPU markers but no per-pass timings on this laptop (Intel driver), so I can't rank the top 5 GPU costs pass by pass.
- With the GPU at 3 ms of a 16.7 ms frame, it isn't what makes caves slow here. Ask the main PC if per-pass numbers are needed.

**Still running underground that the player can't see or reach**

Already culled through `CaveSystem.SurfaceHidden`: flora streaming, far terrain chunks, rain particles, sun shadows, critters, surface creatures, sky birds, surface audio.

What's left, with the ms it would save if culled (profile mean self time):

| What | Cost | Lane |
|---|---|---|
| WeatherField MapJob + LownessJob | 0.5-0.65 ms workers | world |
| WeatherSystem.Update | 0.2-0.3 ms main | world |
| AmbienceSystem (surface parts) | up to 0.15-0.44 ms main | world |
| Mantrap | 0.17 ms main | world |
| AmbientParticle | 0.11 ms | world |
| DunesColossus | 0.06 ms | world |
| CreatureAudio for unheard creatures | 0.1-0.22 ms main | animal |
| Brain for far surface creatures | part of BrainJob's 1.57 ms workers (#59 brain LOD) | animal |

- Together that's about 1 ms main and 1-1.5 ms workers on v2.
- The `-noambientlife -nomantrap -nocolossus -nosandpuffs` run showed no change in frame time. These are worth doing, but they aren't the slow cave.
- Water, settlements and shadow cascades: no measurable cost underground; the GPU total is 3 ms.

**What would fix it (no edits made)**
1. **World, #54: make the cave field cheaper or cache it.** For example, a coarse distance cache per loaded cave chunk, built with the mesh, that the camera, push-out and ReachJob read with one trilinear lookup. Or tighter segment cells on v2.
2. **World, #54: cap the CaveArm steps near walls.** Use a larger minimum step (for example 15-20 cm) and fewer than 160 steps. Or start the trace from last frame's hit distance.
3. **World, item 15: spread cave chunk builds across frames.** At most one FieldJob a frame, and never completed on main. This recovers v2's 1% low.
4. The underground leftovers above: about 1 ms, worth doing after 1-3.

**Relevant to other areas**
- The cave field is shared by the player, camera, creatures (ReachJob, UnderRock) and cave meshing, so a cheaper or cached field helps all of them (#63).
- Push-out and the camera arm also run at solid landmarks (salt towers, arches), so a step cap or cache helps there too.
- The SurfaceHidden pattern that already culls the surface should be reused for underwater (#22) and dungeons (#24).
- Open ground near the v2 cave is the heavier place: GPU 13.7 ms in the profile build, and TraceSystem 2.12 ms main (#55), more than anything in the cave.

Cost: about 59 calls, peak 223k context, $6.4 equiv (item 11 share of today's laptop row).
- Shots: results/shots-q11/
- Data: results/q11.csv, q11-perf-*, q11b-perf-*, q11c-*, prof-762c16a-{cave,approach}-{v2,v1}.txt
- NEXT: re-run the walk test (q11c: `-gocave #1` plus four `-walk` legs) after a #54 or field-cache fix. The v2 east leg (main 14.1 ms, probe 5.0 ms) is the number to beat.

## 2026-11-02: ECS review of main 41a9259 (Zach's request; read-only): core sound, cost is managed main-thread work and one fat component; issues #54-#64
128 scripts, about 60k lines. No edits.
Issues: #54 camera, #55 TraceSystem, #56 shared snapshot, #57 staggered searches, #58 split CreatureState, #59 brain
LOD, #60 Decide table, #61 brain partial files, #62 MonoBehaviour order, #63 managed sampling audit, #64 visible needs.

**Done well:** CreatureBrainSystem is a Burst ISystem with one parallel IJobEntity; neighbours come from a per-frame
grid (20 grid queries; only 3 full scans, all rare: message receiver, ChatsNear, RoostUsers). Creatures draw as
instanced capsules from one GraphicsBuffer (2 draws); the cave cull is Burst (A-22). Terrain, flora and cave meshing
pick results up frames later; spawns are queued and batched.

**ECS problems, most valuable first**
1. `CreatureState` is one ~300-byte component holding motion, posture, mind, needs, chat, task, lair, held item and
   anchor. Every reader copies all of it and the brain writes all of it. Suggest splitting by reader: motion
   (Position, Yaw, Speed, Phase), pose (Lie, Sit, Fly...), mind (Mode, Stage, timers, needs), social/job fields.
2. 10+ main-thread readers each copy every creature's state, body and entity arrays per frame: audio (2), breath,
   burrow, wake, trace, folk talk (2), director (3), mantrap, ambience, render. The first copy in the presentation
   group also completes the brain job. Suggest one snapshot per frame (arrays plus the brain's grid) as a singleton
   shared by all readers; it also covers item 9's FolkTalk double copy.
3. Many "systems" are MonoBehaviours driving entities (FaunaDirector, CaveSystem, FloraSystem, WeatherSystem,
   AmbienceSystem, Mantrap, ThirdPersonCamera). Their order against ECS systems is implicit and they can't chain
   job dependencies. Not urgent.
4. Managed TerrainSource sampling on the main thread (about 16 us each in the player): FaunaDirector 17 call sites,
   TraceSystem 9, AmbientLife 7, CreatureRenderSystem 6, Mantrap 5, AmbienceSystem 5.

**First wins**
- **#54** `ThirdPersonCamera.CaveArm`: up to 160 managed SolidDistance steps a frame (5 cm minimum step in tight
  chambers). Very likely the v2 cave's +1.9 ms (item 8: 1.1 to 3.0 ms). Burst `.Run()` as A-22 did; likely enough to
  bring the v2 cave under 1.3x.
- **#55** TraceSystem: `NoSurface` (2 managed samples) for every creature in range every frame, before checking for a
  footfall. Probably the 0.91 to 1.19 ms of item 9 and the growth on #6.
- Brain: FindPredator/FindAlarm run every frame per hunted animal, StartChat's search at 30% per frame (item 9).
  Staggering them to about 0.2 s by entity loses nothing visible.
- Brain LOD: every loaded creature runs the full brain every frame. Beyond about 80 m, decide every 2-4 frames and
  only ease motion in between.

**Behaviours**
- Sound core: needs (Hunger, Thirst, Tired, Lonely) rise with time, activities ease them, and a weighted roll by
  species taste picks the next activity when the timer runs out.
- Strain: `Decide` has a growing chain of early-return special cases before the roll (hermit, camper, rooted,
  disguised, hunt task, raid, play ring, hearth) and fixed overrides (6, 8, 10, 14) that beat the weights. Their
  order is unwritten behaviour. A (condition, weight) table per activity would keep it tunable.
- CreatureBrainSystem.cs is one 6,277-line job with 22 modes and 13 tasks, and every mode's fields sit in the shared
  component. Partial files by area (town folk, wild, fliers/swimmers, cave life) cost nothing at runtime and ease
  lane collisions.
- The needs are invisible to the player (Lonely only weights play).
Cost: about 30 calls, about $5 (estimate; usage_log lags).

## 2026-11-02: queue item 10 recheck on main 41a9259 (W-27b, PR #53): v2-default host + join PASS; -island v1 PASS
Bench-41a9259, host + local client, no `-island` flag, then `-island v1` on both. 0 exceptions, IndexOutOfRange or
NullReference in all 4 logs. No dialogs.
- **v2 (no flag):** both log generator version 2, layout B seed 8121, **126 landmarks, hash 053C63D5** (as the queue
  expects). The host bakes **once** ("ready at 10.3 s"; was twice in c9f067f), client ready at 11.3 s. Host "in game
  True, players 2", client "players 2", for the whole run. The only disconnect is ClosedByRemote after the client's
  last status line, i.e. the client quitting.
- **v1:** both 102 / 51EA6463, players 2 on both, same clean quit.
- The c9f067f FAIL (host's own client timed out during the second bake) is fixed by W-27b.
- Shots: results q10-default/q10-v1 (host 1, client 2 each; host and joiner stand on the same spawn point).
Cost: about 12 calls, about $2 (estimate; usage_log lags).

## 2026-11-02: queue item 10, host + client on world-features a19bc79 (W-22, v2 default): FAIL, the host loses its own player during the v2 launch bake; v1 PASS
Bench-a19bc79, `-host` plus a local `-join 127.0.0.1`, 75 s, then both quit. Pairs: no `-island` flag (twice) and
`-island v1` on both. 0 exceptions, 0 NullReference, 0 IndexOutOfRange in all 6 logs. No dialogs.
- **No flag (v2), both runs the same:**
  - Both sides log "[island] generator version 2" and **125 landmarks, hash AFA9CC63**, not the queue's
    127 / 831FF2EE. Those were main 0cc3ed7's values; W-21's blur fix and W-24/25 have changed the island since.
    WorldSync agrees ("player 1 has the same world (E28B402B); welcome").
  - **The host's own player is lost.**
    - The host process bakes v2 twice: its server world ("v2 ready at 10.1 s") and its in-process client world
      (14.1 s).
    - The main thread is blocked ~4 s each time, so its local client connection times out: "Net (server):
      connection 1 disconnected (Timeout)", then "Net (client): ... (ClosedByRemote)".
    - From then on the host logs "in game False, players 0" every 10 s. The joining client connects, is welcomed,
      and is "in game True, players 1": it sees only itself.
    - In the shots, each side shows one body at (0,163,0). The joiner spawned on the same spot, not 3 m off as on v1.
  - For the world lane (#23): bake once per process and share it with both worlds, or keep the transport ticking
    (or raise the disconnect timeout) while baking.
  - Not checked: hosting from solo with F2, which creates the netcode worlds after the solo bake. It likely bakes
    again in the new worlds.
- **`-island v1`:** both log "generator version 1 (baked)" and **101 landmarks, hash E128CB1D**, as expected.
  - Host "players 2" while joined; the client sees the host's body 3 m away (client at 3,65,2).
  - Both quit cleanly; the host logs the client's ClosedByRemote at its quit.
- #49 (client NullReference in TerrainSource.HasBiomes when the host quits): not seen in any pair here.
- Shots: `shots/q23/mp-v2-default-vs-v1-a19bc79.jpg` (top: no flag, host left / client right; bottom: v1).
- Cost: about 15 calls, about $2 (estimate: the usage log had not caught up with this session yet).

## 2026-11-02: queue item 9, review of BatFolkBaking/BatFolkMeshes (world fb047dc) and folk chats (animal A-26 ed767e0): nothing urgent, two things that grow with creature count
Read-only review, plus a townwalk cap-60 dev profile of main 0cc3ed7 (before A-26) vs ed767e0 to size them.
**Profile:** frame median 16.69 vs 16.68 ms, p95 18.97 vs 18.88; spikes 3 vs 1. No cost visible in the frame.

**Bat folk (world)**
- `BatFolkBaking.Build` is bake-time only. Attempts are bounded: 60 hut tries, 80 prop tries and a 200-step ceiling
  march per site. On v2 it runs inside the launch bake (`BuildPlaces`, main thread); with 1 site that's a few
  thousand CaveDistance samples, a small part of the 4.4 s v2 bake.
- `BatFolkMeshes` builds every site's two meshes on the first drawn frame, on the main thread, with managed Lists.
  With 1-2 sites and 3-6 huts each it's small. It would be a first-frame hitch only if sites grow to dozens.
  - Build also loops all props for each site (sites × props).
  - Fine now. If sites grow: build only sites within LoadRadius, or group props by site at bake.
- Per frame `DrawBatFolk` does a distance check per site and two `Graphics.RenderMesh` calls. No allocation.
- The meshes are rebuilt when TerrainSource changes (`Release` → `ReleaseBatFolk`), so v2's launch rebake doesn't
  leave v1 meshes behind.
- Small: they cast shadows (`ShadowCastingMode.On`). Underground the only lights are the lantern and glow lights
  (no shadowing sun), so they add shadow-caster draws to the sun's cascades for nothing. `Off` saves 2 draws per
  site in range.

**Folk chats (animal A-26)**
- `FolkTalkSystem` (main thread, managed) now copies the CreatureState, CreatureBody and Entity arrays of *every*
  creature twice a frame: once in the new `WatchChats` and once in `OnUpdate`.
  - Mean 0.046 → 0.068 ms at townwalk; worst frame 0.34 → 2.83 ms (one frame, likely the first
    `FindAnyObjectByType` or a FolkChat log line).
  - It grows linearly with every creature loaded, not only folk.
  - Suggest one copy shared by both passes, or a query limited to upright folk. Better still, have the brain flag
    talkers so the main thread reads only those.
  - `WatchChats` also runs when `Quiet` or with no archive, before the early return.
- Brain chat code: `StartChat` runs a 3×3-cell grid search for "someone who stopped for me" (kind 0) with
  probability 0.3 *every frame* for every ChatFree folk (wandering townsfolk qualify most of the day). That's most
  of the cost.
  - Workers BrainJob mean 0.87 → 0.90 ms (+3%).
  - Cheaper: let the asker set a flag on the asked, or look only when a neighbour's ChatRole changed.
  - `ChatsNear` is an O(all creatures) scan, but only after the rng gate and a successful FindChat, so rare in
    play. Under `-chattest` (rate ×40) it's O(N²) per frame: fine for tests, but don't read tests' frame times.
- GC: chat-end log lines and `FolkTalkLabels.OnGUI` (a `new GUIContent` per line per repaint; IMGUI itself
  allocates). OnGUI is now enabled whenever a "..." bubble is within 30 m, which is often in towns. Small, but it
  is per-frame GC in towns.
- Also seen: `TraceSystem` (main) mean 0.91 → 1.19 ms in this pair. It isn't chat code. Folk standing still to
  talk may leave more traces in view, and townwalk Brain/Trace numbers swing with where animals stand, so take this
  one loosely. TraceSystem was already flagged on #6 (0.05 → 0.40 median since Z-50).
- Cost (items 7-9, with the Builds/ cleanup and rebuild): 69 calls, peak 340k context, $10.4.

## 2026-11-02: queue item 8, smoke main 0cc3ed7 `-island v2` vs v1 (same build, fixed stops): cave CPU 1.8x (FAIL, was 2.6x); load 9.6-10.1 s (borderline PASS)
Bench-0cc3ed7 with the item 7 stops (b/bench-runner c7018e0). Townwalk and glide at cap 60, then townwalk, glide,
cave and caveapproach uncapped; 2 rounds ABBA. 0 exceptions, 0 "Mesh bounds" in all 8 logs. No dialogs.

| stop (mean of 2) | v1 avg / 1% low / cpu / gpu | v2 avg / 1% low / cpu / gpu |
|---|---|---|
| townwalk cap 60 | 59.3 / 43.2 / 16.9 / 8.6 | 58.7 / 37.8 / 17.1 / 8.8 |
| glide cap 60 | 56.2 / 29.0 / 17.8 / 9.9 | 58.5 / 36.9 / 17.1 / 9.2 |
| townwalk uncapped | 85.0 / 50.7 / 11.8 / 8.1 | 87.5 / 44.0 / 11.4 / 8.0 |
| glide uncapped | 47.0 / 19.1 / 21.3 / 12.3 (see below) | 79.0 / 41.5 / 12.7 / 8.0 |
| cave uncapped | 172 / 109 / **5.8** / 1.5 | 96 / 39.3 / **10.5** / 2.4 |
| caveapproach uncapped | 91.8 / 41.0 / 10.9 / 7.1 | 66.8 / 31.6 / 14.9 / 10.8 |

- **Cave CPU: v2 10.5 ms vs v1 5.8 = 1.8x, over the 1.3x limit.** Item 6 (e3ab4af) was 19.7 vs 7.7 = 2.6x.
  - A-22's Burst cave cull worked: `CreatureRenderSystem` is no longer in the v2 cave's growth list.
  - Dev profile, cave uncapped (median frame 7.2 → 11.3 ms), what grew on v2:
    - `ThirdPersonCamera.LateUpdate` +1.8 ms (1.1 → 3.0, main thread): the biggest single item, also in item 6.
      The v2 chamber is tighter, so camera collision probes are the likely cause; for the world/camera owner.
    - BrainJob workers +1.4 ms (0.25 → 1.7) and main +0.3: more animals near this cave on v2.
    - Cave meshing still running during the measure (ColumnJob, FieldJob, MeshJob 0 → 0.7-0.8 ms each on workers):
      v2's cave chunks are still streaming 10 s after placement (180 chunks vs 83 on v1).
    - `AmbienceSystem.Update` +0.3, `CaveSystem.Update` +0.25, `InteractionInputSystem:ReachJob` +0.24.
- **Load:** the game's own clock reads "[island] v2 ready at" 9.6, 9.7, 9.7 and 10.1 s (bake 4.4-4.7 s: generate
  ~1.2, places ~3.3). That's under 10 s in 3 of 4 runs. The bench's "world ready" (streaming settled) is 14.5 s vs
  8.7 s on v1. Pass on the stated measure, narrowly.
- Same island checks: v2 127 landmarks, hash 831FF2EE. v1 stops at the same spots as item 6.
- Bat folk on v2: "[batfolk] caves 3, picked 1 (of 37 caves)".
- **v1 glide uncapped 47 fps in the smoke is void:** both rounds were slow (CPU and GPU both up). Two checks don't
  reproduce it:
  - Dev profile: median frame 11.6 vs 11.3 ms on e3ab4af.
  - ABBA rerun of the same Bench builds: 0cc3ed7 82.6/82.2 fps (1% low 36.2/35.1), e3ab4af 84.0/82.9 (35.1/36.5).
  - Read it as a laptop state during those two runs, not the code. v2's interleaved glide runs were fine, so the
    v1 vs v2 glide row above isn't a v2 win either.
  - Mantrap.Update (+0.19 ms main) is new on main's glide.
- Shots: `shots/q23/bench-stops-v1-top-v2-bottom-0cc3ed7.jpg` (item 7's sheet; the same build and stops).
- Cost: see item 9's line.

## 2026-11-02: queue item 7, bench stops on island v2: fixed on b/bench-runner c7018e0; every place stop lands right on v1 and v2
- **town/townwalk:** the route matched the first place whose name *contains* "HumanVillage". On v1 that is
  "Nearest settlement (HumanVillage)"; on v2 the nearest settlement is a DwarfMiningCamp, so it fell through to
  "Sonoran: HumanVillage". Now: "Nearest settlement (HumanVillage)" if present, else the HumanVillage landmark entry
  by exact name. v1 is unchanged (town 136,-475, townwalk ends 366,-406). On v2 town is a desert human village
  (3066,-913; huts in the shot).
- **Correction to item 6:** v2's townwalk started at a village then too. The walk is 180 m (30 s at 6 m/s), so it
  ends past the edge on both islands (the v1 end shot is open grass too). I judged item 6 from the end shot.
- **lake:** the fixed spot (1328,1730) is from v1. It is now used only where the point 40 m ahead is water,
  else the auto-found "Lake shore" (v2: 88,-247, over the spawn lake). v1 still uses the fixed spot.
- **cave/caveapproach:** a real cave on v2 (surface hidden 1.00, 180 cave chunks, lantern lit; the approach is a
  surface walk 100 m out). No change needed.
- forest, nightgrove, storm, river and stormshore land in their biome or feature on both islands. On v2,
  stormshore shares lake's "Lake shore" spot, as on any island whose nearest lake is the spawn lake.
- 0 exceptions, 0 "Mesh bounds", no dialogs. Shots: `shots/q23/bench-stops-v1-top-v2-bottom-0cc3ed7.jpg` (10 stops
  each, HUD on).
- Cost: see item 9's line.

## 2026-11-02: queue item 6, world-features e3ab4af `-island v2` vs default (same build): v2 cave stop 2.4x slower on the CPU (CreatureRenderSystem 10.5 ms); townwalk doesn't reach a town on v2
Same exe, `-island v2` or not. Townwalk and glide at cap 60, then townwalk, cave, caveapproach and glide uncapped; 2
rounds, alternating which island went first. 0 exceptions, 0 "Mesh bounds" in all 8 logs. No dialogs.

| stop (mean of 2) | v1 avg / 1% low / cpu / gpu | v2 avg / 1% low / cpu / gpu |
|---|---|---|
| townwalk cap 60 | 59.1 / 39.6 / 16.9 / 8.3 | 57.0 / 28.0 / 17.6 / 9.3 |
| glide cap 60 | 55.4 / 28.6 / 18.0 / 10.1 | 57.3 / 32.7 / 17.5 / 9.6 |
| cave uncapped | 131 / 88.9 / 7.7 / 1.8 | **51 / 29.6 / 19.7 / 2.7** |
| caveapproach uncapped | 95.7 / 53.4 / 10.5 / 6.6 | **68 / 34.6 / 14.7 / 11.3** |
| glide uncapped | 82.6 / 36.4 / 12.1 / 8.3 | 95.0 / 47.5 / 10.5 / 7.3 |

- **Load:** world ready at 15.8 s on v2 vs 8.5 s on v1 (all 4 runs each): +7 s to generate.
- **The stops aren't the same places**, so read this as "v2's own stops", not a regression on one spot:
  - townwalk on v2 ends at 3279,-839 in desert scrub, not in a village (v1: 366,-406, grassland by the town).
    `JumpTo("HumanVillage")` or the route's walk needs a look on v2. Its 1% low 23.2 in round 1 is one 273 ms frame.
  - glide starts at the same coordinates (1197,359) over different ground; v2 is lighter there.
  - cave: v1 -2677,-697 (MesaCanyonlands); v2 -1653,2380 (Mountains, the HUD says rain outside).
- **Cave profile (dev builds, uncapped, `PUNCHY_ISLAND=v2` for the v2 run):** frame median 8.9 ms on v1, 21.0 on v2.
  - `CreatureRenderSystem` on the main thread: mean **2.2 -> 10.5 ms**, almost all self time (its PoseJob is 0.14),
    so main-thread work or a wait in its update, not the posing job. For the animal/world lanes: many more creatures
    near this cave, or something in the render system scaling badly there.
  - `ThirdPersonCamera.LateUpdate` 1.2 -> 3.0 ms (tighter cave, more camera collision probes?).
  - BrainJob (workers) 0.3 -> 1.8 ms: more animals near this cave on v2.
- Shots: `shots/q23/island-v1-v2-townwalk-cave-approach-e3ab4af.png` (left v1, right v2: townwalk, cave, approach;
  HUD on).
- Cost: 15 calls, $1.2 (session $6.9).

## 2026-11-02: queue item 5, host/join on animal-behaviours b5e9715: PASS, a remote player's roll shows the somersault (#48)
Bench-b5e9715, host `-host -nohud` plus a local client `-join 127.0.0.1`. Host shot list: wait 45 s, then
`-actiontest roll:Dog,Dog,Dog` (6 rolls, 0.53-0.56 s, 3.9-4.4 m). Client: shots every 0.2 s from cameras placed
relative to its spawn.
- **Players 2** on the host from 20 s to the end; **0 exceptions, 0 IndexOutOfRange** on host and client in all 5
  runs (`results/q5`, `q5b`-`q5e`). Both players quit on their own (no Punchy.exe left).
- **Seen on the client:** the host's Dog jogs (c-067), goes over onto its back with its legs up (c-068) and tumbles
  through inverted (c-072), then lands and jogs on, 10-15 m down the path. So the Roll pose byte reaches the remote
  avatar and drives the somersault.
- Framing took 5 tries (now in TESTING.md): `-watch Dog` doesn't pick up a remote avatar, `-goto` doesn't move a
  joined client, and fixed `-camlook` points near the origin were inside the slope.
- Not checked here: descent while flying or swimming seen remotely (#48 lists it); one body only (Dog).
- Shots: `shots/q23/48-remote-dog-roll-b5e9715.png` (client crops: jog, legs up, inverted, landed).
- Cost (items 4 and 5): 37 calls, peak 210k context, $2.3 (session $5.7).

## 2026-11-02: queue item 4, smoke animal-behaviours b5e9715 vs v0.11.0 4eea658: PASS on fps; TraceSystem 8x on the main thread (Z-50)
Townwalk and glide at cap 60 and uncapped, 2 rounds alternating, then a townwalk cap-60 dev profile on each build plus
profdiff. 0 exceptions, 0 "Mesh bounds"; same end spots (townwalk 366,-406, glide 1197,359). No dialogs.
- **The runner's NOT CLEAN (glide cap 60 1% low 35.5 -> 29.4) is one base outlier:** v0.11.0's round-2 glide ran
  unusually smoothly (1% low 57.3, worst 17.9 ms, 0 slow frames). Its other three glide runs today are 27.9-28.8, and
  b5e9715 gives 29.1/29.6, which is the v0.11.0 baseline (29.6). Glide at cap 60 has sat near 30 since v0.8.
- Townwalk cap 60 1% low 34.6 vs 34.8 (v0.11.0 vs b5e9715); uncapped 43.0 vs 48.0; glide uncapped 37.2 vs 36.7.
  Average fps and GPU level everywhere.
- **Profile (townwalk cap 60, 1750 frames each):** frame median 16.68 on both, p95 19.26 vs 19.67, spikes 3 vs 1.
  - `Punchy.Fauna.TraceSystem` (main thread): median **0.05 -> 0.40 ms**, mean 0.20 -> 0.50, worst frame 3.6 -> 4.5.
    The only change since v0.11.0 is c0b26df (Z-50 gallop): each leg now calls `b.LegPhase(ref leg, b.Sprint(...))`
    and `FootAt(..., s)` per frame. For the animal lane: Burst-compile or cache that per creature (the brain already
    knows the gait), as 0.3 ms on the main thread at townwalk will grow with the residents.
  - CreatureAudioSystem mean 0.18 -> 0.22 (Z-51 voices); BrainJob, Brain.Advance unchanged.
- Cost: see item 5's line.

## 2026-11-02: queue item 3, #31 houses lit wrong on the laptop: graphics tier, GPU and quality level ruled out; cause not found from laptop shots alone
Bench-8d83102, `-benchstops town -benchhour 12|18.5|22 -gfx Medium|High -nohud`, 1280x720, 0 exceptions.
- **What the laptop runs:** D3D11 on the GTX 1060 Max-Q (Zach's own v0.11.0 Player.log, 2026-11-01, shows the same,
  so it isn't the Intel GPU). Quality level "PC" (PC_RPAsset): the Standalone default, and no UnityGraphicsQuality is
  saved in the registry, so both machines get the same pipeline asset. Medium: shadow distance 50 m, 4 cascades,
  2048 main shadow map, soft shadows. The game's own saved window is **1280x720 windowed** on the laptop.
- **Tier ruled out:** noon Medium and High look the same and measure the same (stone wall luma 45.9 at both).
- **What looks off at this view:**
  - The stone house walls are flat and dark at noon: luma 46 vs 70 for the hut walls beside them and for a far stone
    house. The near house's front wall and its quoin face read the same, so no light/shade difference between faces.
  - Faint bright dotted lines along the stone house's wall base and corner seams, at noon and at 22:00 (sheet below).
  - At 22:00 most windows are dark (at 18:30 they are lit): probably by design, noted only.
  - No Z-32-style banding (lit edges, dark centre) at 18:30 or 22:00 in clear weather.
- **Likely causes, not confirmed:** (1) a different build: Zach's laptop log is v0.11.0 (has the Z-32 flat walls),
  so check which build the main PC played; (2) resolution: 1280x720 here vs probably larger on the main PC, which makes
  thin seams and shadow texels wider on screen (the dotted seam lines fit that); (3) the stone material's darkness at
  noon if the main PC shows the same wall brighter.
- **To settle it:** the same shot on the main PC from the same build: `-benchroute -benchstops town -benchhour 12
  -gfx Medium -nohud -weatherseed 4 -benchshots <dir>` at 1280x720, then once at its usual size. The same picture
  means it's resolution or taste; a brighter wall means a machine difference (then GPU and driver from Player.log).
- Shots: `shots/q23/31-stonehouse-medium-12-18h30-22.png` (stone house, noon / 18:30 / 22:00, Medium, 2x crop).
- Cost: 14 calls, $0.8 (session total so far $3.3).

## 2026-11-02: queue item 2, Dunes sand puffs and night sky GPU on the laptop: PASS (+0.9 ms and +0.35 ms)
Bench-8d83102 and Bench-4eea658, uncapped, Medium, alternating order, 2 runs each, 0 exceptions, same spot
(3236,-1416).
- **Sand puffs:** `-place Dunes -benchstops here -camrel 0,4,-8,0,1,12`, with and without `-nosandpuffs`.
  GPU 4.9/4.7 ms on vs 3.9/3.9 off: **+0.9 ms** (main PC +1.0). Pass (< +1.5). Avg fps level (116 vs 112).
- **Night sky:** same spot at 00:00, camera on the big moon (`-camrel 0,2,0,-11,83,-57`, moon in frame behind
  cloud). GPU 11.3/11.0 ms on 8d83102 vs 10.9/10.7 on v0.11.0: **+0.35 ms**. 1% low 66.6/69.5 vs 57.0/61.1.
- Cost: included in item 3's line.

## 2026-11-02: queue item 1, smoke world-features 8d83102 vs v0.11.0 4eea658: PASS, no change beyond noise
Townwalk and glide at cap 60 and uncapped, then cave and caveapproach uncapped. Two rounds each, alternating which
build ran first. No dialogs open, 0 exceptions, 0 "Mesh bounds" in all 12 logs. End spots are the same on both builds
(townwalk 366,-406, glide 1197/1198,359, cave -2677,-697, caveapproach -2773,-726), so it's the same island.

| stop (1% low, mean of 2) | 4eea658 | 8d83102 |
|---|---|---|
| townwalk cap 60 | 33.8 | 34.2 |
| glide cap 60 | 28.4 | 30.1 |
| townwalk uncapped | 47.2 | 43.1 |
| glide uncapped | 39.0 | 35.8 |
| cave uncapped | 90.0 | 95.2 |
| caveapproach uncapped | 48.0 | 46.2 |

- Average fps is level at every stop (within 1-2%), and so are CPU and GPU frame times (GPU glide 10.0/10.1 vs
  10.0/10.3 ms). The uncapped 1% lows split both ways, inside the usual round-to-round spread (4eea658 alone ranges
  44.2-51.8 on caveapproach).
- Worst frame 45 ms over all runs: none of the ~500 ms stalls. Glide cap 60 slow frames 9/6 vs 16/14 (new is better).
- The sand puffs and night sky aren't on these routes (route stops are at noon); item 2 measures them.
- No gate shots (a smoke, not a release gate); item 2/3 shots cover the new visuals.
- Cost: 55 calls, peak 124k context, $2.2 (includes preparing items 2-5).

## 2026-10-31: slot 8, a4a5378 (animal c810406 InteractRpc + world b39522b) vs cand/v0.11 21f1193: no regression visible; the ~520 ms stalls are back on BOTH builds and swamp the 1% lows
0 exceptions in every log. Every stop ended at the same spot (townwalk 366,-406, glide 1197/1198,359).

### ABBA, cap 60 (a4a5378, 21f1193, 21f1193, a4a5378)
| run | townwalk 1% low · worst ms | glide 1% low · worst ms |
|---|---|---|
| a4a5378 r1 | 18.6 · 524 | 7.9 · 532 |
| 21f1193 r1 | 18.1 · 512 | 6.4 · 529 |
| 21f1193 r2 | 18.3 · 524 | 5.0 · 522 |
| a4a5378 r2 | 36.5 · 36 | 3.9 · 527 |

- Means: glide 5.9 vs 5.7, townwalk 27.6 vs 18.2. The runner says NOT CLEAN on J-006 for both builds alike.
- Every difference comes from how many ~500–530 ms single-frame stalls landed in a stop:
  - 1 in every townwalk except a4a5378 r2 (so a4a5378 "wins" townwalk);
  - 2–5 in every glide.
- With the stall windows set aside, the 5 s window worsts are the same on both builds (24–46 ms). Avg fps is level:
  glide 51.5 vs 51.6, townwalk 58.5 vs 57.8. GPU is level: 10.4 vs 10.2, 8.7 vs 8.9.
- **Verdict: no regression from c810406 visible.** It needs a stall-free session to measure finer. The a4a5378 smoke's
  NOT CLEAN was the same stalls: its base b39522b had them just as badly (glide cap 60 1% low 8.0 / 3.2).

### MP joined townwalk on a4a5378 (host cap 60 plus a local client, one run)
players 2 at both stops, 0 exceptions and 0 IndexOutOfRange on host and client. Town 1% low 7.5, townwalk 3.6, worst
528/525 ms: the same stalls again, so the fps numbers can't be read. Avg 53.5 / 47.2 fps, host cpu 18.7 / 21.2 ms, GPU
11.5 / 11.6.

### The ~520 ms stalls: environmental, and now caught live
- The same build 21f1193 ran glide cap 0 clean at 16:20 today (worst 38–40 ms, 4 runs). At 21:07–21:39 every run of it
  stalled.
- So it's the machine's state, not the code: the world smoke 3976d17/247c690, the a4a5378/b39522b smoke and this slot
  all had them, on old builds and new alike.
- What was different on the machine this time:
  1. **A Windows Security Alert (firewall) dialog** has been open since 19:48 for
     `C:\users\zache\projects\punchy\builds\bench-a4a5378\punchy.exe` ("NewBlocked 4"). It covers the whole a4a5378
     smoke and this slot. It wasn't clicked; that's for Zach.
     - The runner normally runs games only from Builds/Bench, so something launched that archive exe directly.
     - `dialogs` does list it ("rundll32: Windows Security Alert"), but the column was cut off in our logs.
  2. Media Player and Chrome are open (Zach may be back on the machine).
- Suspect 1, to test once Zach has dealt with the dialog: rerun 21f1193 glide cap 60 twice. If the stalls go, it was
  the pending firewall prompt (or the game's blocked network calls timing out at ~0.5 s).
- SmartByteTelemetry has 2900 s of CPU since boot but used only 0.19 s in a 10 s sample, so it's unlikely.
- Gate rule from here: before a run, read the full `dialogs` output, not just the start of the line. With a dialog
  open, results that show ~500 ms worst frames are void.

## 2026-10-31: slot 7, cand/v0.11 21f1193 glide cap 0 spikes: not reproduced in 4 runs (2 dev captures, 2 release); no system to name
0 exceptions. No dialogs. Every glide ended at 1197/1198,359.

### Dev captures (Profile-21f1193, `-benchstops glide -fpscap 0 -profilestop glide`)
| capture | 1% low | worst ms | frames > 33.3 ms (of 2000) | 5 s windows (worst ms/slow frames) |
|---|---|---|---|---|
| r1 | 32.7 | 46.8 | 1 | 33/0 47/3 37/1 32/0 16/0 16/0 35/1 |
| r2 | 34.1 | 36.6 | 1 | 30/0 33/1 32/0 37/1 30/0 25/0 29/0 |

- The single spike frame in each capture is the ordinary streaming hitch seen since slot 5:
  - workers busy with BuildChunkMeshJob (+2 / +5 ms), GroundDetail SampleGroundJob (+7 ms in r1), FloraSystem
    PlaceJob, SurfaceGlow ScanJob (one call of 22–24 ms) and WeatherField MapJob;
  - the render thread waiting on them (Semaphore.WaitForSignal +11 / +19 ms);
  - main thread extras: Mono.JIT +3.2 ms (r1); GC.Collect plus CollectIncremental +4.7 ms incl (r2).
- Animal side: TraceSystem (paw prints, the trace-seat change in 77c03e9) costs 0.05–0.07 ms per frame. Its single
  worst frame was 3.1 ms, on r2's spike frame. That's a minor contributor, not a 50–80 ms cause.
  CreatureRenderSystem PoseJob +3 ms and BrainJob +1.5 ms on spike frames: the same as before the merge.
- Nothing on either branch shows a 50 ms+ call or frame.

### Release builds, same session, ABBA (Bench-21f1193 vs Bench-a66c520), glide cap 0
| run | 1% low | worst ms |
|---|---|---|
| 21f1193 r1 | 35.3 | 37.7 |
| a66c520 r1 | 35.6 | 37.5 |
| a66c520 r2 | 36.6 | 35.3 |
| 21f1193 r2 | 36.3 | 39.5 |

### Read
- The candidate is level with a66c520 today: 35.8 vs 36.1, with worst frames 38–40 ms on both.
- Across all 4 of today's candidate runs, no 5 s window went over 47 ms. The gate's 50–80 ms windows (3 of 4 runs
  earlier) didn't come back.
- So I can't attribute them to the world side (b39522b) or the animal side (77c03e9): neither shows a cost that would
  explain them.
- With the 500 ms stalls in the world smoke (slot 5) also gone the next session, the likelier story is an outside load
  on the laptop during the gate session, not the merged tree. If it shows up again, check `tasklist` during the run
  and take a capture on the spot.
- Not a blocker by these numbers.

## 2026-10-31: slot 6, world-features b870008 (Z-30 grain 632f479 + more timber 69f3185) vs 3976d17: no change beyond noise; no GPU cost from the grain in town
0 exceptions and no NaN-bounds lines in any run. No dialogs, and no 500 ms spikes (worst frame 45 ms). Every stop ended
at the same spot (town 136,-475, townwalk 366,-406, glide 1197/1198,359).

Setup: cap 60, Medium, stops town (a still view, which gives the GPU read), townwalk and glide. ABBA: new then old in
round 1, old then new in round 2. The in-between (`-gfxset view=0.8,detail=0.75,shadows=0.8`) ran on b870008 once per
round. Mean 1% low in fps (the two rounds), then GPU ms:

| | town | townwalk | glide |
|---|---|---|---|
| 3976d17 Medium | 46.3 (44.7, 47.9) · 9.8 | 42.4 (45.1, 39.7) · 8.8 | 31.1 (30.6, 31.5) · 10.7 |
| b870008 Medium | 45.3 (42.4, 48.2) · **9.5** | 41.8 (42.6, 41.0) · 8.9 | 31.0 (30.8, 31.2) · 10.9 |
| b870008 in-between | 57.5 · 9.4 | 46.2 · 8.5 | **35.6** (35.7, 35.5) · 10.4 |

- Runner verdict: "no change beyond noise" (townwalk 42.4→41.8, town 46.3→45.3, glide 31.1→31.0).
- Grain GPU cost in town: none measurable. The town still view reads 9.5 ms against 9.8 (−0.3; r1 9.4 vs 9.5, r2 9.6
  vs 10.0). Townwalk is +0.1.
- Caveat: in the town shot the timber is mid-distance (results/shots-s6-new-r1/town.png). A close-up on a timber
  building would be the worst case for a per-pixel grain, if wanted.
- In-between on b870008: glide 35.6, matching slot 5's 34.0 on 3976d17. It still holds J-006 on glide, and Medium
  still sits at about 31.
- Runner: b/bench-runner is at db3c1f2 (`-benchday`). It's a no-op without the flag. b870008 was built with it, with
  no trouble.

## 2026-10-31: slot 5 on world-features 3976d17: in-between gfx holds J-006 on glide; the 500 ms spikes didn't come back on any build (not code); 32-slot worm marks inside the noise
0 exceptions and no NaN-bounds lines in any run. No dialogs. Every stop ended at the same spot (townwalk 366,-406, glide
1197/1198,359, worm 869,1500).

### Gfx in-between probe (`-gfxset view=0.8,detail=0.75,shadows=0.8`, logged as "(Custom)"), cap 60, 2 rounds ABBA
Mean 1% low in fps (rounds), then GPU ms:

| 3976d17 | townwalk | glide |
|---|---|---|
| Low (0.6/0.5/0.6) | 57.5 (56.9, 58.1) · 6.7 | 57.4 (57.1, 57.6) · 7.8 |
| **In-between (0.8/0.75/0.8)** | **51.9** (46.6, 57.2) · 8.8 | **34.0** (33.5, 34.4) · 9.5 |
| Medium (1/1/1) | 41.6 (41.6, 41.6) · 8.8 | 28.9 (29.3, 28.4) · 10.3 |

- The in-between holds J-006 (1% low ≥ 30) on glide in both rounds, by about 4 fps. Medium fails it in both. Glide's
  worst frame: 37–38 ms in-between against 42 ms on Medium; slow frames 3 against 11.
- The in-between is a fair candidate for the default if Low looks too sparse. It has no headroom to spare on glide, though.
- Shots: results/shots-g5-<Low|Mid|Med>-r<n>/.

### The ~500 ms glide spikes from the world smoke: not reproduced, on any build; they look like the machine, not the code
- The world smoke's spikes were 502–527 ms each, several per stop at cap 60, in townwalk too. They hit **247c690 just
  as hard as 3976d17** (247c690 glide cap 60 1% low 4.9 and 5.5). So they were never a z-p2 regression.
- This slot ran Medium, cap 60, townwalk + glide, ABBA, on all three builds: 3976d17, 247c690, and 0c637a9 from slot 4.
  **The worst frame in all 10 route runs and both dev captures is 43 ms.** Glide 1% low was 28.9 / 30.8 / 29.2 on
  3976d17 / 247c690 / 0c637a9, which is the same within noise.
- What the spikes are:
  - Not GC: GC counts per stop were the same in spiking and clean runs (21–35).
  - Not shader compile: that happens once, on the first visit, not 5–6 times a stop and in both builds alike.
  - The near-constant ~0.5 s length points to an outside stall during that session, such as another process holding
    the GPU or disk (an editor import, a scan). I can't name it from the logs.
  - If it recurs: run `tasklist` / check for an open editor during the run, and take a dev capture
    (`bench.sh profile`) at the same time.
- What does cost glide on Medium is the ordinary 35–43 ms hitches, 2 dev captures of 3976d17 glide (23 and 11 frames
  > 33 ms of 2000). On those frames, against normal frames:
  - The workers fill up with streaming jobs: BuildChunkMeshJob +10–14 ms, GroundDetail SampleGroundJob +6–9,
    WeatherField LownessJob +4–8 and MapJob +4, FloraSystem PlaceJob +2–4.
  - The render thread waits on them (Semaphore.WaitForSignal +11–14 ms).
  - That's streaming load as the glide crosses new ground, and it's what View trims.
  - Small main-thread extra: IMGUI GUIStyle.GetDimensions/GetMeshInfo +1–1.6 ms on spike frames (the debug panel).

### Worm marks at 32 slots (3976d17 = 247c690 + z-p2, worm 407f998): +0.2 ms GPU mean, inside the noise
Same fixed frame (`-place Glacier -camlook 900,200,1525,869,186,1500 -benchwait 40 -benchstops here`), cap 0, Medium,
ABBA, a fresh player each run.

| | avg | 1% low | GPU ms (r1, r2) |
|---|---|---|---|
| natives only | 95.4 | 66.5 | 9.1 (8.9, 9.3) |
| spawned worm | 93.3 | 67.2 | 9.3 (10.6, 8.0) |

- Mean +0.2 ms, under the 0.3 bar, but the paired swings are +1.7 and −1.3, so this frame can't resolve 0.3 ms.
- Why it's noisy:
  - The same coordinates now draw about 9 ms GPU (5 ms in slot 4) on this island.
  - Native worms wander through the frame: natives r2 has a worm and its trail in the middle of the shot, while the
    spawned worm is off-frame in on r1.
- Read: no sign of a cost above the noise. A tighter check would need a frame with no native worms nearby (another
  biome, with `-spawn SnowWorm:1`).

### Builds
- Profile-3976d17 (dev) added.
- Bench-3976d17, Bench-247c690 and Bench-0c637a9 were reused.

## 2026-10-30: slot 4 (item 17 worm mask, wakes, gfx preset sweep on 0c637a9): masks cut most of the cost; Low is the only preset with J-006 headroom
0 exceptions in every run. No dialogs. The first worm pass was lost to a bug in my script: a variable named W
clobbered the runner's log path. Everything below is from the clean re-run.

### Item 17, worm-mark mask (local merge 2de3029 = shader 5198212 + feeder bd5e706): +0.4 ms GPU, just over the 0.3 bar; ~2/3 of the old cost gone
Same fixed frame as before (`-place Glacier -camlook 900,200,1525,869,186,1500 -benchwait 40 -benchstops here`), cap 0,
2 rounds in ABBA order, a fresh player each run. The spawned run's shot shows the worm's line of bumps in view.

| | avg | 1% low | GPU ms (r1, r2) |
|---|---|---|---|
| 2de3029, natives only | 114.7 | 71.2 | 5.1 (5.2, 5.0) |
| 2de3029, spawned worm | 111.3 | 68.2 | **5.5** (5.5, 5.4) |
| d60e085 (old, no mask), spawned worm | 107.3 | 68.7 | 6.3 (6.4, 6.3) |

- Masked spawned vs natives: **+0.4 ms** (+0.3 and +0.4 by round). Old unmasked vs the same natives: +1.2 ms. So the mask
  removes about 0.8 ms, two-thirds of the cost.
- The natives-only baseline moved between sessions (5.9 last slot, 5.1 now): which native worms come near varies. Treat
  the 0.3 bar as about ±0.2 ms. The result is marginal, not a clear fail.

### Wakes, mask cut (local merge b70dbfa = Water.shader 4f597bf + feeder 138874c): +0.3 ms, at the bar, but the frame is weak
`-goto -1087,-1600 -camlook -1078,41,-1594,-1115,31,-1600 -benchwait 30 -benchstops here`, cap 0, ABBA. 8 spawned ducks
vs natives: GPU 10.8 → 11.1 ms (+0.4, +0.2), avg 84.0 → 82.9, 1% low 43.8 → 55.4 (noise).
- Caveat: the camera sits inside a tree canopy (foliage across the top left), and the ducks are only specks on the
  far water (results/shots-wakemask-on-r2/here.png). It isn't a close-up worst case. A camera out over open water
  would make a fair re-check.

### Graphics preset sweep, world-features 0c637a9 (creature ranges in): cap 60, 2 rounds (Low→Ultra, then Ultra→Low)
`-gfx` was confirmed in each log ("Graphics: view 0.6 detail 0.5 shadows 0.6 (Low) (from the command line)"). Every
stop ended at the same spot on every run. Each cell is the mean 1% low in fps (the rounds' range where it matters),
then GPU ms:

| preset (view/detail/shadows) | townwalk | glide | caveapproach | forest |
|---|---|---|---|---|
| Low (0.6/0.5/0.6) | 58.0 · 6.7 | 57.7 · 8.1 | 57.9 · 6.0 | 57.4 · 6.0 |
| Medium (1/1/1), as tuned | 41.1 (33–49) · 8.8 | **30.7 (27.8, 33.6)** · 11.5 | 35.4 (33–37) · 8.7 | 50.6 · 8.9 |
| High (1.3/1.5/1.4) | 39.2 · 11.4 | **27.8 (27.9, 27.6)** · 15.0 | **33.0 (29.7, 36.2)** · 10.9 | 45.2 · 9.3 |
| Ultra (1.7/2/2) | **29.3** · 13.6 | **24.1** · 21.8 (avg 38) | 32.3 (28.5, 36.1) · 13.4 | 43.1 · 14.4 |

- **J-006 (1% low ≥ 30 at cap 60):**
  - Low holds it everywhere with large headroom (57+).
  - Medium holds only on average: glide is 30.7, one round was 27.8 (10.5 slow frames a stop). That's the same
    borderline as the v0.10 gate.
  - High fails glide in both rounds and caveapproach in one.
  - Ultra fails glide and townwalk.
- **Recommendation for this laptop: default Low.** It's the highest preset that holds J-006 with headroom. Medium has
  none on glide.
  - Glide is the limit, and on Medium it's CPU-bound as well (cpu 18.3 ms against the 16.7 ms frame). So View (the
    streaming and draw range) is the likely lever.
  - If Low looks too sparse, an in-between `-gfxset view=0.8,detail=0.75,shadows=0.8` is the next thing to test (glide
    and townwalk, 2 rounds).
  - Forest and town have room to spare at Medium; glide sets the ceiling.
- Shots for every preset and stop: results/shots-gfx-<preset>-r<n>/.


## 2026-10-30: slot (items 15, 16, worm marks): cave-chunk passes (small), Blocked cache passes (big), worm marks over the bar
0 exceptions and 0 NaN-bounds lines in every run. No dialogs. BProfileTools (b/bench-runner 9a4c308) now adds maxCallMs and
maxFrameMs columns: the longest single call, and the most one frame spent on a marker.

### Item 16: Brain.Blocked, blocked-cache 3d874dc vs bc89606: PASS, about 30x cheaper
Marker builds 3481353 (bc89606) and 74d8148 (3d874dc), townwalk cap 60, 2 dev captures each, ABBA. All four ended at
366,-406. Workers + main:
| | bc89606 r1 | bc89606 r2 | 3d874dc r1 | 3d874dc r2 |
|---|---|---|---|---|
| animals (Net status mean) | 100 | 97 | 98 | 106 |
| Brain.Blocked calls/frame | 39.4 | 39.0 | 38.9 | 44.7 |
| Brain.Blocked µs/call | 17.7 | 30.9 | **0.98** | **0.69** |
| Brain.Blocked ms/frame | 0.70 | 1.21 | 0.04 | 0.03 |
| BrainJob inclusive ms | 2.18 | 2.59 | **1.47** | **1.67** |
| worst Blocked frame ms (maxFrame) | 4.9 | 6.1 | 0.2 | 0.3 |
- Behaviour is the same: the call rate, animals and end spot all match. Town look (`town` stop, same frame):
  results/shots-town-{3481353,74d8148}/town.png. The buildings, props, paths and fences are identical; only the
  animals moved. The old cost swung 18–31 µs between captures, as in item 14; the new one is under 1 µs either way.

### Item 15: cave-chunk 256e6a9 vs bc89606, caveapproach: PASS (compiles, cut edge identical), but nowhere near 5x at frame level
- A/B, cap 0, ABBA, 2 rounds, same end -2773,-726: avg 97.5 → 98.0, **1% low 52.5 → 59.6**, GPU 6.5 → 6.3 ms,
  worst frame 27/30 → 25/21 ms.
- Dev captures (3481353 = bc89606 plus the Brain marker; no terrain change), 2000 frames, 0 spikes in both:
  | BuildChunkMeshJob | bc89606 | 256e6a9 |
  |---|---|---|
  | mean ms/frame (workers) | 0.700 | 0.661 |
  | busiest frame, ms (maxFrame) | 36.8 | **30.9** (−16%) |
  | longest single batch, ms (maxCall) | 15.6 | 14.4 |
  The job is an IJobParallelFor, so a call is one worker's batch; the busiest frame is the closest reading of the mouth
  chunk's total. It's 16% lower, not 5x. Either the mouth chunk isn't the busiest chunk on this walk, or the cut's
  skipped samples weren't most of its cost. A per-chunk timing log would settle which.
- Cut edge: a mouth spot `-benchspot -2822,-648,90` (in the slot at the mouth, facing in) and one above it
  (-2812,-660,60). Shots are identical on both builds: results/shots-mouth2-{bc89606,256e6a9}-*/spot.png. Both
  builds show a few flat grey shards at the cut edge (bottom right of the slot shot). They're already in bc89606,
  so they're not from this change; worth a look by the world lane. The `-place CaveNetwork` and caveapproach end
  shots don't frame the mouth, but they also match.

### Worm marks (92a9602 shader + 18f1b3b feeder, local merge d60e085): +0.5 ms GPU, over the 0.3 ms bar
The Glacier has native snow worms, so I fixed the camera instead of using -watch:
`-place Glacier -camlook 900,200,1525,869,186,1500 -benchwait 40 -benchstops here`, cap 0, a fresh player each run, 2 rounds
in ABBA order. Same frame in every shot (results/shots-wormcam-*). The `on` shots show the spawned worm's trail of bumps
across the snow; the natives-only shots show no marks in view.
| | avg | 1% low | GPU ms (r1, r2) |
|---|---|---|---|
| bc89606, no SnowWorm species (no marks; upper bound) | 115.2 | 52.0 | 5.5 (5.4, 5.5) |
| d60e085, natives only | 110.1 | 50.5 | 5.9 (6.1, 5.7) |
| d60e085, -spawn SnowWorm:1 (trail in view) | 107.8 | 51.0 | **6.4** (6.3, 6.5) |
- Spawned against natives-only, same build and frame: **+0.5 ms GPU** (+0.2 and +0.8 by round). Against bc89606:
  +0.9 ms, but that's an upper bound, since animal-behaviours also adds creatures, traces and the worms' bodies.
  The spawned run also draws the worm's own body and clods, but they're a few small meshes. Most of the gap should be
  the mark loop.
- Verdict: over the 0.3 ms bar. The 32-slot cap or the per-chunk bounds test is worth doing. The same command lines
  re-run the check: Bench-d60e085 is archived, and the merge is on the local branch bench/worm-merge.
- Caveat: there's no readout of _PunchyBurrowerCount, so how many marks each run fed is unknown. Natives within 150 m
  could have fed some marks in the natives-only runs too.


## 2026-10-30: slot (item 14, close-up ducks, F-40 smoke): Blocked is the town cost; ducks and F-40 pass
0 exceptions and 0 NaN-bounds lines in every run below. No dialogs.

### Item 14: Brain.Sample split with Brain.Blocked (91f8957 on 6cfedaf vs 32ec6ff on 7bd0768): item 13's +30% is Blocked noise, not Sample
One dev capture per stop, cap 60, workers + main summed per frame. Townwalk ended at 366,-406 in both, with 96/98
animals. Forest is the OakGrove stop: it ended at -2363,-400 in both, with 67 animals each.
| | townwalk 91f8957 | townwalk 32ec6ff | forest 91f8957 | forest 32ec6ff |
|---|---|---|---|---|
| Brain.Moving calls (movers) | 61.8 | 60.5 | 47.1 | 42.1 |
| Brain.Sample µs/call | 17.8 | 9.5 | 1.89 | 1.66 |
| Brain.Blocked µs/call (calls/frame) | 37.8 (45.7) | 19.7 (35.8) | 0.10 (29.9) | 0.11 (26.6) |
| Sample minus Blocked, µs/call | 2.75 | 3.00 | ≈1.85 | ≈1.62 |
| Blocked share of Sample time | 85% | 68% | ~2% | ~2% |
| BrainJob inclusive ms | 3.16 | 2.39 | 0.77 | 0.72 |
- **Sample's own work is flat** (2.75 vs 3.00 µs in town; forest flat or lower on 7bd0768). All the variance is
  `FloraPlacement.Blocked`. It costs 20–38 µs a call near town plots and props, and 0.1 µs away from them.
- **This run reversed item 13.** The old Sample is now the dearer one: 17.8 vs 9.5 µs, where item 13 had 8.7 vs 11.2.
  Both captures ended at the same place with the same animal count. So a single townwalk capture can swing Blocked
  2× on where the movers happen to stand. Item 13's +30% was this noise (and the layout), not Sample's code.
- **Target:** in town, Blocked is 68–85% of Sample and about 0.7–1.7 ms of BrainJob a frame. That matches the world
  lane's suspect: it re-runs Place() for nearby plots and props on every call. A baked or per-cell cached
  blocked answer is the fix to try (CLAUDE.md: per-region work belongs in a baked table).
- The forest control isn't fully identical. The grove behind matches, but one near prop differs: a standing stone
  in 91f8957 and an oak in 32ec6ff (results/shots-i14-*-forest/forest.png). It's a different island, but there are no
  plots, so Blocked costs the same 0.1 µs in both.
- The marker branches (wip/brain-blocked*) remain profiler-only.

### Close-up ducks (item 6 worst case): 793f103 vs 3d1893d, cleared
`-benchspot -1087,33,-1600,270 -goto -1087,-1600 -spawn Duck:8 -benchwait 12`, cap 0, ABBA plus a third round (BA).
"spawned 8 Duck" is in every log. Shots of the same frame each time; B's r2 shot shows a duck about 10 m out, with
wake rings.
| | avg | 1% low | GPU ms | worst ms |
|---|---|---|---|---|
| 3d1893d (3 runs) | 91.0 | 68.1 | 10.1 | 18 / 22 / 14 |
| 793f103 (3 runs) | 92.8 | 49.9 (65.9 without r2) | 9.6 | 21 / **728** / 21 |
- B's r2 had a single 728 ms frame in the stop's last 5 s window: one slow frame, no exception, and GC the same as
  the others (19). It didn't recur in r3 (worst 21 ms), so I'm treating it as an outside stall, not the wake loop
  (which would cost every frame). Without it the 1% lows are within noise, and avg and GPU are flat or better.
  **Swimmer wakes are cleared.**

### F-40 smoke: world-features tip bc89606 (trodden mask + early-out + worm marks) vs 6464ce7: PASS
bc89606 compiled (Bench-bc89606 is archived for item 15's A side). Stops: `-place OgreCamp` measured by `here`, plus
townwalk; cap 0, ABBA, 2 rounds. Same end x,z: -365,-4132 and 366,-406.
| | 6464ce7 | bc89606 |
|---|---|---|
| OgreCamp avg / 1% low / GPU | 111.1 / 69.9 / 4.9 | 112.2 / 72.8 / 4.8 |
| townwalk avg / 1% low / GPU | 86.9 / 50.4 / 7.2 | 86.4 / 49.0 / 7.2 |
| worst ms (camp, town) | 18–20, 27–30 | 18–19, 28–29 |
- Within noise. The camp shots match frame for frame; the stalls are too far from this viewpoint to show the
  trodden patch. A look check of the patch belongs to the world lane's stall shots.


## 2026-10-30: item 6, swimmer wakes (world-features 793f103 vs 3d1893d) at a duck lake: no cost found
- **Duck lake:** `-place Grassland` then `-place clearlake` → shore (-1087, 33, -1600), facing west (270) over a
  clear lake 1.8 m deep (Temperate biome, ducks listed). `-watch Duck` found a duck on it 35 m out, floating, with
  wake rings: results/lake2-793f103/g-duck.png. FoggyMarsh's "clear lake" landed on a glacier, FungalForest's is a
  1.6 m pond, Temperate's was the sea shore. Bench stop: `-benchspot -1087,33,-1600,270`.
- **A/B (spot, cap 0, ABBA, 2 rounds, same island: same end x,z and same frame):** avg 91.8 → 96.5, 1% low 56.2 → 59.0,
  GPU 9.7 → 9.4 ms, worst 24/22 → 19/31 ms, 0 exceptions. No change beyond noise.
- Caveat: neither end-of-stop shot shows a duck, so the swimmer loop (cost only within 12 m of a swimmer) may not have
  had a duck near the visible water for much of the stop. For a worst case, frame a duck up close (`-watch Duck`, or
  `-spawn Duck` at the shore) and re-bench.

## 2026-10-30: item 13, Brain.Advance split (wip/brain-sample 7bd0768 vs its e4010f2 twin 6cfedaf): dearer sampling, not more movers
Townwalk cap 60, one dev capture each (~1775 frames, 1 spike frame each), 96 animals in both (Net status mean).
Workers + main summed, per frame:
| | 6cfedaf (e4010f2) | 7bd0768 | Δ |
|---|---|---|---|
| Brain.Moving calls (movers) | 56.8 | 55.8 | −2% |
| Brain.Advance calls | 111.6 | 105.9 | −5% |
| Brain.Sample ms | 0.97 | 1.19 | +0.22 |
| Brain.Sample µs per Advance call | 8.7 | 11.2 | **+30%** |
| Brain.Phase ms | 0.116 | 0.115 | 0 |
| Brain.Advance self ms (Accelerate + heading) | 0.025 | 0.025 | 0 |
| BrainJob inclusive ms | 2.00 | 2.35 | +0.35 |
- The growth is each creature's terrain sampling getting dearer (Sample per call +30%), not more movers and not
  Accelerate. Most of the remaining +0.13 is likely the decision markers' own cost (Brain.Predator/Alarm/Playmate…
  exist only on 7bd0768). Suspects: the TerrainSource paths Advance samples (F-22's RiverSampling.Lake per sample, the
  CaveSystem changes); a per-path marker split (ground / water / cave floor / walls) would name it. [animal, world]
- Aside: 7bd0768 had one 19 ms NetMenu.Update on its single spike frame (solo); not seen before, one frame.
- Files: results/prof-{6cfedaf,7bd0768}-townwalk-cap60-i13.txt. 7bd0768 predates the test-hooks merge: rebase before
  merging.

## 2026-10-30 03:52: v0.10.0 PUBLISHED (supervisor)
- https://github.com/Skeleman/Punchy/releases/tag/v0.10.0 (pre-release, "Punchy v0.10.0 (Windows preview)"), tag v0.10.0 =
  rc/v0.10.0 519fb5b25a2964a0cc3aa6f1f5f1ae9dfb48abe2, on Zach's go ("release a v10 before 7AM").
- Punchy-v0.10.0-windows.zip: 237 files, 47 MB, SHA-256 fee646b3f586285ecd5322742ffcb609095ccecc6cbe6297a42b6742e46953d7
  (re-downloaded and matched). pack.py check: backslash False, donotship False, exe True; the .from marker was held out.
- Notes: release/release-notes-v0.10.0.md. The Unity slot goes to the world lane next (F-40), then item 13.

## 2026-10-30: v0.10 light gate: rc/v0.10.0 519fb5b PASS (c7410cc gated in full, plus a re-gate after the arch NaN fix)
Results in Punchy-wt/Tools/bench/results (gate-c7410cc-*, gate2-c7410cc-*, regate-519fb5b-*).
- **Release build: PASS.** `C:\Users\zache\Projects\Punchy\Builds\Play` = 519fb5b (.from), built with a throwaway
  editor-only ReleaseBuild.Build (BuildOptions.None, the project's own settings: product Punchy, D3D11 then D3D12),
  no bench files in the tree or the player. Solo boot/quit ×2 with no -screen-* flags: exit 0, Direct3D 11.0,
  0 exceptions, 0 "Mesh bounds" / 0 NaN-bounds lines (logs ~4.5 KB). pack.py should drop
  Punchy_BurstDebugInformation_DoNotShip.
- **Arch NaN (found in this gate, fixed in 519fb5b): PASS.** c7410cc logged 10 arch meshes with NaN y at startup and
  then ~165 "bounds contain NaN" lines a second (~45 MB of Player.log an hour), from G-29 79b4221; absent from 807ee9f
  and v0.9.0. 519fb5b: 0 in every log (release, smoke, arches, town). Shots: regate-519fb5b-arches/icearch.png
  (glacier, 721,726: an ice arch crossed by a smaller one) and sandstonearch.png (MesaCanyonlands, 1266,185) both render.
- **Smoke vs v0.9.0 39f5558 (c7410cc, same island, cap 60 and 0, ABBA): PASS, borderline J-006 within v0.9 spread.**
  townwalk cap 60 1% low 41.6 (v0.9 41.1), cap 0 46.2 (45.5); glide cap 0 37.6 (36.2). Glide cap 60 30.5/27.7
  (mean 29.1) vs v0.9.0's own rounds 28.4-31.2: borderline J-006, within v0.9 spread. 0 exceptions.
  519fb5b vs c7410cc cap 60: townwalk 41.0, glide 31.8, no change beyond noise, 0 exceptions.
- **Looks (c7410cc): PASS.** Day town/cave/lake/river, night town/nightgrove, spire spot (still on the
  ElectricWastelands spires, arcs showing): all render, exit 0, 0 exceptions. Day town on 519fb5b the same.
- **MP host/join (c7410cc): PASS.** players 2 in every joined row, 0 IndexOutOfRange, 0 exceptions; joined town /
  townwalk 1% low 26.8 / 26.8 (v0.9: 26.8 / 25.1).
- **MP climb (item 12): PASS.** Client framed by a -shotlist re-applying the host's -goto/-camlook (command-line ones
  are overridden by the join's NearHost placement). The remote Monkey is upright on the Oak trunk mid-climb, hands and feet
  on the bark, a little off it (known animal-lane item): gate2-c7410cc-climb/shots/climb-11.png, climb-01.png.
- **MP mantle: PASS.** Flat-top picker: boulder rim 1.5 m. The remote Human stands on the boulder top (ledge-07.png),
  back down and up again across the run; the 0.8 s hands-on-rim pose fell between shots. 0 errors either side.

## 2026-10-30: cave-hitch A/B (world-laptop/cave-hitch d14f556 vs 1cbdd9f): PASS
d14f556 = MaxBuildFrames 4 → 60, one commit on 1cbdd9f. Cap 0, ABBA, 2 rounds, same island (every stop ends at the
same x,z in both builds), 0 exceptions.
| Stop | 1cbdd9f worst ms (r1/r2) | d14f556 worst ms | 1% low | avg |
|---|---|---|---|---|
| caveapproach | 138.6 / 108.0 | 24.3 / 27.2 | 30.9 → 52.5 | 96.3 → 98.2 |
| dash | 43.5 / 72.1 | 41.0 / 24.5 | 21.5 → 41.2 | 79.1 → 95.9 |
| glide | 40.2 / 33.4 | 41.3 / 33.1 | 36.1 → 37.2 | 79.7 → 79.7 |
- The caveapproach hitch (item 10) is gone: no 5 s window over 25 ms (before: 77-139 ms spikes in 5 of 7 windows).
  Dash slow frames 26 → 2. Glide unchanged.
- Shots at the end of every stop (results/shots-cave-<c>-r<n>/) match between builds: same framing, no holes,
  no missing terrain or flora. They're end-of-stop frames only, so pop-in mid-flight isn't covered; nothing
  late at the glide or dash end points.

## 2026-10-30: test hooks on wip/test-hooks 0019e8a: tree PASS, ledge picked a hut roof
- Tree (`-body Monkey -place tree:Oak -autowalk`): lone Oak at (-267, 27), 10 m; on the trunk at t23 (y 40,
  ground 36), in the crown at t27, back down by t38. Watch: `-goto -264.2,37.7 -camlook -264.2,40.3,33.7,-267.2,38.8,26.7`.
- Ledge (`-place ledge -autowalk`): rim (152.0, 27.3, -439.2), 1.3 m up, but it's the eave of a conical thatched
  hut roof in HumanVillage; the Human stands on the roof from t21 to t71 and never drops back. zache-db is fixing the
  picker (flat tops) before the rc. [animal]

## 2026-10-30: item 12 (remote climbing/mantle MP check): BLOCKED on a test hook
Both wire fields are on animal-behaviours (6d70335 Climbing, 2a8007a Rim). To check them two-instance on the laptop I
need the host to climb and mantle by itself, then frame it from the client with -camlook + -shot. Today
`-place tree:<species>` puts the player 6 m east of a lone tree without turning them to it, so `-autowalk` (camera at
tree + (16, 7, -16)) passes ~4 m from the trunk, and nothing brings a player to a ledge. Asks: [animal]
- `-place tree:<species>` facing the trunk (yaw towards it), so `-body Monkey -place tree:<X> -autowalk` climbs; and
- a `-place ledge` (or `-place mantle`): in front of a rim about 1.5-2.5 m up, facing it, so `-autowalk` mantles.
- Log the trunk/rim position (as now), so the client can `-goto` near it and `-camlook` at the climber.
With those I'll run host (`-host -body Monkey ...`) + client (`-join 127.0.0.1 -camlook ... -shot`) and post shots of
the remote climber and the remote mantle, plus the join log check.

## 2026-10-30: items 10 and 9 on world-features 807ee9f
- **Item 10, caveapproach hitch: still there, and it's terrain meshing, not caves.** Release build, 2 rounds:
  worst frame 108.9 / 102.8 ms (1% low 31.9 / 33.3, avg ~96). Dev capture: 2 spike frames of 2000. On them
  Workers BuildChunkMeshJob runs 39.3 ms (0.6 normally) while the main thread waits 37.8 ms (WaitForJobGroupID,
  inside the SimulationSystemGroup, 38.5 ms inclusive): TerrainStreamingSystem completing a mesh batch on the main
  thread, most likely the forced Complete after MaxBuildFrames (audit S2). CaveSystem.Update 0.18 ms and
  GroundDetail 0.13 ms on those frames; HideFarChunks doesn't show. Fix direction: smaller batches while the focus
  is moving, or don't force-complete (let the next batch start late); the 4-frame cap meets a 39 ms batch. [world]
- **Item 9, lake stop: done.** The lake stop is now PlaceAt (1328, ground, 1730), yaw -90 (b/bench-runner d5be092,
  pushed). Shot `shots/lake-807ee9f-1328-1730.png`: on the shore, open water west, trees and reflections, no
  water walls. Lake avg 104-107 fps; round 2 had one 156 ms frame (1% low 27.3 vs 59.1 in round 1), probably
  the arrival's streaming; it'll show again in later lake runs if it's real.
- Item 6 (swimmers) still needs a duck lake in FoggyMarsh/FungalForest/Grassland/Temperate: post x, z.

## 2026-10-30: item 8 follow-up, where the BrainJob growth could be (for the animal lane)
- Population isn't it: Net status "animals N" in the townwalk capture logs (mean once spawned) e4010f2 131,
  a4521a2 133, 6d0fbff 134, ea3bdad 136: ~4% more animals vs ~30% more BrainJob. So it's cost per animal.
- Read-only notes from a laptop session (zache-db, standing by; not the lane): in e4010f2..ea3bdad Advance itself
  only gained CreatureMotion.Accelerate (a min/compare). TerrainSource's diff there only touches the water path
  (F-22: one extra RiverSampling.Lake per sample in lake/bay areas); CaveSystem +164 is untested. Another
  candidate is the mix: with Accelerate, animals reach their targets later, so more are moving (paying for Advance
  and its terrain samples) on a given frame.
- To separate them: a `Brain.Sample` marker round the SampleHeight/SampleGround/Walled calls inside Advance, plus a
  per-frame count of moving creatures, on a commit that cherry-picks onto both e4010f2 and ea3bdad. Post the two
  commits in BENCH-QUEUE and I'll capture townwalk cap 60 on both. [animal]

## 2026-10-30: items 11 and 8 on animal-behaviours ea3bdad
- **Item 11, host/join: the join fix works.** 0 IndexOutOfRange and 0 exceptions on host and client, both rounds
  (the client in game with 2 players, 78-141 animals). Host cap-60 with the client joined vs solo: town 1% low
  32.6 → 26.6, townwalk 32.3 → 26.0 (same-laptop client), the same as 39f5558 and 4ec2cab.
- **Item 8, cap-60 townwalk, Brain.* split (one capture each):** Workers BrainJob a4521a2 2.24 → 6d0fbff 2.18 →
  ea3bdad 2.03 ms (e4010f2 was 1.58). Inside it, **Brain.Advance is 1.16 ms (57%)**, and on spike frames it grows
  1.15 → 1.82 ms. Brain.Predator 0.04 and Brain.Alarm 0.03; Hunter, Chaser, Playmate, Needs, Fidget, Decide and
  EaseGait are each under ~0.03 ms (below the analyzer's cut-off, so not listed). About 0.8 ms of BrainJob is
  outside every marker. Main-thread BrainJob 0.50 → 0.31 ms and CreatureWakeSystem 0.44 → 0.04 ms (fixed).
  So the remaining growth over e4010f2 is in Advance (movement), not in the decisions. Note: the markers'
  own overhead (~100 Advance calls a frame per worker) is in these numbers. [animal]

## 2026-10-30: for the supervisor: takeover checklist
Zach says the project passes to kind-possum when your tokens run out. What the laptop can't do yet (no gh, no
milestone page access, no messaging to main-PC lanes) and what the final handoff should cover:
`TAKEOVER-CHECKLIST.md`.

## 2026-10-30: status, items 8-11 running now (results here in ~75 min)
Order: 11 host/join on ea3bdad (the TrySpawn join fix), 8 cap-60 townwalk capture on ea3bdad split by the Brain.*
markers vs a4521a2, 10 caveapproach capture on 807ee9f (worst frame's top markers), 9 the new lake stop (1328, 1730,
yaw -90) shot on 807ee9f. Item 6 still needs a duck lake spot (FoggyMarsh/FungalForest/Grassland/Temperate): post x, z.

## 2026-10-29 late: item 5 profile, item 7 lake stop (BLOCKED)
- **Item 5, cap-60 townwalk profile 6d0fbff vs a4521a2:** the EaseGait trim cut posing, not the brain.
  Workers PoseJob 1.50 → 1.29 ms (slow frames 5.2 → 3.8). Workers BrainJob 2.24 → 2.18 ms: still ~0.6 ms over
  e4010f2's 1.58, so a4521a2's brain growth is mostly still there. New on the main thread: CreatureWakeSystem
  0.44 ms a frame (0 before). Spikes 4 → 3 of ~1750. One capture each. [animal]
- **Item 7, lake stop: BLOCKED, the spot still has water walls on 4ec2cab (F-22 is in it).** Placing the player
  facing 45° from (1118.5, 1553.5), so the camera sits about (1110, 1545), puts them on ground at 134 m, ~7 m under
  the lake surface (141.4) 10 m away, in a pit walled by vertical sheets of water: `shots/lake-4ec2cab-1118-1553.png`.
  F-22's own note says water walls went 6 → 2; this glacier lake looks like one of the 2. The route change is not
  pushed (the lake stop is unchanged). Needs from world: either the remaining walls fixed here, or a player
  position (x, z, yaw) on ground at lake level. I'll rerun item 6 (swimmers) on the same spot once there is one;
  say whether ducks live on that lake. [world]

## 2026-10-29 night: queue items 4-6
- **Item 4, host/join on world-features 4ec2cab: BUG, in v0.9 too.** Join works (players 2, 70-131 animals), but in
  round 2 the host threw IndexOutOfRangeException in FaunaDirector.TrySpawn twice, right after "player 2 has the same
  world; welcome". Cause (code unchanged since 39f5558; timing-dependent, so the gate's two rounds missed it):
  GatherPlayers() rebuilds `players` every frame, but `counts` (players x species) is only resized in Despawn(), every
  0.25 s. For up to 0.25 s after a joiner first reports a position, players.Count is 2 while counts fits one, and
  TrySpawn(playerIndex 1) reads `counts[offset + index]` past the end (`FaunaDirector.cs:769/791` at 4ec2cab). The
  exception aborts that frame's director update; nothing crashes. Fix: resize counts in GatherPlayers (or bail in
  TrySpawn when `(playerIndex + 1) * species.Count > counts.Length`). [animal]
  Perf with the client joined, same as v0.9: host cap-60 1% low town 40.5 → 26.8, townwalk 36.8 → 25.1 (same-laptop
  client). The cached WorldSync/NetMenu queries show no difference at this resolution.
- **Item 5, smoke 6d0fbff vs a4521a2: no change beyond noise** (townwalk@60 avg 58.9 → 59.0, 1% low 37.3 → 37.1;
  townwalk@0 86.1 → 87.5; glide@60 1% low 30.4 → 29.0, the J-006 note again). At cap 60 the frame rate can't show
  BrainJob's worker time; a cap-60 townwalk profile of 6d0fbff vs a4521a2's is running and will follow here.
- **Item 6, lake ab 3d1893d → 793f103: PASS but inconclusive.** lake@0 ~147 fps both, 1% low 96.5 → 90.2 (within
  noise). The lake stop now ends at a glacier lake (1103, 132, 1540), and the bench log has no swimmer count, so
  this probably didn't exercise the water shader's swimmer loop at all. Needs a spot with ducks: post a
  `-benchspot x,y,z,yaw` at a duck lake and I'll rerun it. [world]
- Runner: one lake row read GPU 5,679,878,978 ms (FrameTimingManager garbage); the compare ignored it, but parse
  should drop GPU values above a sane bound.

## 2026-10-29 night: v0.9 light gate on rc/v0.9.0 39f5558: PASS (no regression vs v0.8.0 ba87e14)
Laptop, D3D11, 1280x720 windowed, weatherseed 4, ABBA. Same island as ba87e14 at these stops (townwalk (366, -406),
glide (1197, 359)). 0 exceptions anywhere.
- **Smoke vs ba87e14: no regression.** The runner prints NOT CLEAN only for J-006: glide cap 60 1% low 29.5 → 29.4
  (under 30 on v0.8.0 too). Uncapped: townwalk avg 84.8 → 87.8, glide 77.9 → 80.6; GPU equal or lower.
- **Looks (screenshots checked):** day town, cave, lake, river; night town, night grove. Flora drawn, night windows
  and fires lit, cave lantern lit, fireflies. 0 "Invalid combination", 0 exceptions. The lake stop now ends on a
  glacier shore (1103, 132, 1540) with the camera against the ice: not a defect as far as I can tell, but a useless
  look shot; worth moving the stop or checking that spot in play. [world]
- **Host/join (first end-to-end run of `bench.sh mp`): works.** Both rounds the client got in game with 2 players and
  78-171 animals; 0 exceptions on host and client; the client's one "serverTick rolled back" warning came with
  the laptop running both games. Host cap 60 with the client joined vs solo: town 1% low 39.3 → 25.2, townwalk
  35.0 → 25.8, GPU +1.3-1.9 ms. The client runs on the same laptop (480x270, cap 20), so this overstates a real
  remote join, and there's no v0.8.0 MP number from this runner to compare with. Recorded as the MP baseline for
  queue item 4.
- Not run (not part of the light gate): quit-clean checks, the D3D12 look, the spires.

## 2026-10-29 evening: queue item 3 (capture for audit item 1, a4521a2)
Dev build, uncapped, town and townwalk (plus item 2's townwalk cap-60). No frames > 33 ms in either uncapped capture
(town median 11.4 ms, townwalk 11.2), so "doubles on spike frames" couldn't be tested here.
- **Pose half: confirmed.** The main thread runs a slice of PoseJob itself on every frame (1.0 call/frame, 0.15-0.25
  ms): CreatureRenderSystem schedules and Complete()s it in the same update, so the main thread waits on posing.
- **Brain half: present, not yet attributed.** The main thread runs BrainJob work on 20-60% of frames (0.2-0.6
  calls/frame, mean 0.14-0.50 ms), so something completes BrainJob mid-frame. CreatureRenderSystem's update is
  0.93-1.10 ms inclusive vs ~0.6 self, consistent with it being the waiter, but the analyzer's flat markers can't say
  which system calls Complete. Needs the hierarchy view (or a ProfilerMarker round the render snapshot) to confirm.
- **Size:** main-thread job waiting (JobHandle.Complete inclusive) 1.1-1.7 ms a frame, ~10-15% of an 11 ms frame.
  Workers are mostly idle, so overlapping brain and pose would mainly shorten the main thread, not free workers.
  Worth doing, not urgent. Not a cause of spikes at these stops. [animal]

## 2026-10-29 evening: queue item 2 (re-queued after the pause)
Laptop, 1280x720 windowed, weatherseed 4, noon, ABBA. Same island throughout (townwalk ends (366, -406), glide
(1197, 359), cave (-2677, -697), caveapproach (-2773, -726)); 0 exceptions. Smoke/ab take no screenshots.
- **smoke world-features 28d51e9 vs 0c0198a: PASS**, no change beyond noise. 1% lows townwalk@60 40.5→36.2 (one noisy
  round: 29.6 then 42.9), townwalk@0 45.6→48.8, glide@60 29.1→30.4, glide@0 35.7→33.6.
- **ab cave, caveapproach 0c0198a → 28d51e9: PASS.** cave@0 ~155 fps, 1% low 101→100; caveapproach@0 ~93 fps,
  1% low 29.1→29.2. Not new, but caveapproach has a ~110 ms worst frame on both builds, every round: a hitch on the
  way into the cave, worth a capture.
- **townwalk cap-60 profiles, e4010f2 → animal-behaviours a4521a2:** the townwalk fix works: Main AmbientLife.Near
  0.99 → 0.12 ms. But Workers BrainJob mean 1.58 → 2.24 ms (slow frames 5.5 → 8.1), PoseJob 1.40 → 1.50, and Main
  BrainJob 0.38 → 0.50 (the main thread running brain work while it waits: see audit item 1, being captured as
  item 3). Frame median 16.7 both (capped); p95 17.7 → 19.0; spikes > 33 ms 2 → 4 of ~1760. One capture each, so the
  brain growth is a lead, not a verdict: likely the G-10/F-06..F-12 behaviour changes in a4521a2. [animal]
