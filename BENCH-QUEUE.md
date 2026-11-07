# Bench queue (supervisor → kind-possum)

Since J-041 kind-possum runs on a different Claude account, so cross-session messages may not reach it.
The supervisor adds requests here; kind-possum pulls before each job and reports in BENCH-REPORTS.md
(newest first, one short entry per queue: gate results and real regressions only).

## Setup (once, after the account switch)
- Pull `coordination` and run `sh hooks/install.sh <fixed folder outside your trees>` (J-041 commit telemetry).
  Make one test commit on a b/ branch and check ~/.punchy-telemetry/commits.csv gets a row.

## Standing rule: uncapped (Zach, 2026-11-03)
- Profile and bench uncapped (no frame cap, vsync off). Capped runs hide CPU/GPU headroom. Use a cap only if a card
  asks for one, and label it.

## Standing rule: cost log (from 2026-11-02, main-account supervisor)
- After every bench job or session end, and once now for the backlog: pull `coordination`, run
  `python tools/usage_log.py --machine laptop`, and commit `COSTS/laptop.csv` (message "Costs: laptop <date>").
  It reads this laptop's local transcripts with a script (no model call, so no tokens spent) and rewrites the file
  in full, so extra runs are harmless. Don't summarise it; the supervisor reads the CSV for its milestone report.
- Put each job's cost line in its BENCH-REPORTS entry: `Cost: <calls> calls, peak <k> context, $<equiv>`.

## Queue (top first). 2026-11-02: everything older was cleared (v0.9-v0.11 era, done or stale)
1. Smoke world-features 8d83102 vs v0.11.0 (townwalk, glide, cave, caveapproach; ABBA). Same island (hash E128CB1D).
   New since v0.11: Dunes sand puffs, blue moon with drifting tendrils, rarer fading shooting stars, bench-runner merge.
2. Dunes GPU on the laptop: `-place Dunes` fixed view, with and without `-nosandpuffs`, 2 runs each. Main PC saw
   about +1 ms (4.1 vs 3.1). Pass = under +1.5 ms here. Also night sky GPU at one fixed view (moon in frame) vs v0.11.0.
3. Issue #31, houses lit wrong on the laptop: one town shot at noon and one at dusk on world-features 8d83102, plus
   the quality level and light/shadow settings in use. Name the likely cause in BENCH-REPORTS (no edits); the world
   lane fixes it.
4. Smoke animal-behaviours b5e9715 vs v0.11.0 (townwalk cap 60 plus one profile; ABBA). New: dodge/roll, fish
   sprint, wall climb (A-04), fear height check while flying.
5. Host/join on animal-behaviours b5e9715: a remote player's roll (B/Circle) shows the somersault on the other
   screen; no join errors.
6. Later: when world-features gains `-island v2` (batch W-05), smoke `-island v2` vs default on the same build.

## Added 2026-11-02 (afternoon). main = 0cc3ed7 (both lanes merged, #51). Zach: v2 becomes the default (W-22, pending)
7. Bench stops on v2, done on your b/bench branch: townwalk has to reach a village on v2 (item 6 ended in desert
   scrub). Also check cave/caveapproach land in a real cave on v2. Every stop must work on both islands (rule 12c).
8. Smoke main 0cc3ed7, `-island v2` vs v1, same build, ABBA, with your fixed stops. Compare against item 6: A-22 moved
   the cave cull to Burst (main PC: 7.1 → 0.33 ms) and W-15/W-16 cut bake time (v2 ready ~7.2 s on the main PC).
   Pass = v2 cave CPU within 1.3x of v1, and v2 load under 10 s on the laptop.
9. Review only, no edits: `BatFolkBaking`/`BatFolkMeshes` (world) and FolkChat in CreatureState/Idle (animal A-26):
   per-frame allocations, main-thread work, anything that grows with creature count. Findings go in BENCH-REPORTS.
10. W-22 has landed (world-features 5016957, "v2 default"): host + client with no `-island` flag. Both must log
   126 / 053C63D5 on world-features after W-27 (main 0bd91cb: 125 / D709D95C); then `-island v1` on both gives 102 / 51EA6463 (main: 101 / E128CB1D). No join errors.

## Added 2026-11-03. main = 762c16a (world lane merged, PR #66; animal lane not merged yet)
11. Slow caves on the laptop (Zach: "worth looking into the slow caves on the laptop"). Build main 762c16a to
    Builds/Bench. Bench cave and caveapproach, `-island v2` and v1, cap 60, ABBA vs v0.11.0. One profile per
    island at the cave stop. Name the top 5 main-thread and GPU costs inside a cave with ms each. Earlier leads:
    camera LateUpdate +1.8 ms on v2 (main now uses the sphere-trace CaveArm; `-perflog` prints probe calls/ms),
    CreatureRenderSystem 10.5 ms main thread at the v2 cave stop (item 6), and per-chunk cave timing (item 15).
    Say which lane owns each cost (world: caves/camera/cave cull; animal: creatures). No edits. Pass target
    (Zach 2026-11-03: "I would expect that caves run faster than open ground. They have much more limited scope."):
    cave fps and 1% low BETTER than open ground on the same build. So also list what still runs or draws inside a
    cave that the player can't see or reach: surface terrain chunks, grass/flora, sky/weather, water, far creatures,
    shadow cascades, settlements. Give the ms each would save if culled underground.

## Added 2026-11-03 (evening). main = 27bf8c1 (PR #77)
12. Box collision cost (#79, W-67). world-features 52b4af5 vs main 27bf8c1, `-island v2`, standard route plus the
    cave stop (-goto 530,958) and a wharf town if the route passes one. Per-frame ms and the top main-thread costs,
    with PlayerMovement, ReachJob and ThirdPersonCamera.LateUpdate shown separately. Interim number: W-67b is still
    changing this code, so expect a re-run. If 52b4af5 won't build, report that and stop.

## Added 2026-11-03 (night). main = 913d1b2 (PR #89 box collision, #90 P-1, #91 W-68 v3 flag)
13. Merged-main check after job 12. main 913d1b2 vs 27bf8c1, `-island v2`, uncapped, same stops as q12/q12b (route, hut cave floor `-goto 530,45,958`, FishWharf steps). Expect the hut-cave gap (ThirdPersonCamera, TraceSystem) closed now that A-45 and the collision are both on main. Numbers only unless something regressed.
14. Memory check (Zach asked, 2026-11-03). On main 913d1b2 in the editor, with the Memory Profiler package: snapshot after the island bake, then enter and exit play mode 10 times, snapshot again; list anything that grew (leaks) and the top 10 holders of editor memory (is the island bake a big part?). Also note editor RAM idle vs after bake vs after 10 plays, and a player build's RAM on v2 and v3. Numbers and the top-10 list only; no fixes.
15. Burst and jobs audit (Zach asked, 2026-11-03). Current origin/main, `-island v2`, uncapped, the q13 route. In the Profiler Timeline: how busy the worker threads are compared with the main thread per frame (ms and %), and the top 10 main-thread systems by ms, each marked Burst or not, jobified or not. Find ISystems / jobs missing [BurstCompile] (grep plus the Burst Inspector), managed SystemBase code on hot paths, .Run() or .Complete() calls that force main-thread syncs every frame, and systems doing work that could be IJobEntity / ScheduleParallel. Also check the editor's Burst settings (compilation on, safety checks and leak detection off for the measurement). Report a ranked list of the 5 biggest wins with an estimated ms each and the owning lane (world, animal, player, shared). No code changes; read-only.
- [ ] 16 (2026-11-03) Memory recheck on main 97d40dd (PR #95, W-73 bake blob fix): 10 enter/exit plays on v2 and v3, Total Used per cycle; player build RAM after 3 host/leave cycles. Read-only. Owner if still leaking: world.
- [ ] 17 (2026-11-04) Parallel island bake (W-76), Zach asked. Ready: world-features b262269 (W-76) vs b262269~1. v2 and v3: per-stage [bake] ms, total bake ms, "ready at", worst frame during the bake, landmark + ground hashes (must match). Laptop numbers, 3 cold starts each. Read-only.

## Job 18 (2026-11-04): W-75 + A-51 main-thread wins
Bench world-features 096ec00 (W-75) and animal-behaviours b527dc1 (A-51) against main 9d34275 (Bench build each, uncapped): q13 route + hut cave stop, frame ms, worst frame, main-thread ms for CreatureRender, InteractionInput, TerrainStreaming, GroundDetail, Flora, AmbientParticle; plus "world ready at" (W-75 logs it; PUNCHY_LOADBOOST=0 for the off case). 3 runs each. Job 17 (W-76) follows when W-76 posts its SHA.

## Job 19 (2026-11-04): startup streaming tail
Bench world-features 4e3b63c (v3, also v2), laptop player, uncapped, 3 cold starts. Break down the ~20 s from island ready to "[stream] world ready": per-system main-thread ms per frame, chunks per frame, frames spent waiting on jobs vs idle, and which systems gate "world ready" (terrain mesh, flora, ground detail, colliders, landmarks). Rank the 3 biggest levers with estimated seconds saved and the owning lane. Also: the v3 landmark hash reads 108/3A0DC7BB in the player but 108/4D2643D7 in the editor probe; say which code path each uses if you can see it. Read-only.

## Job 20 (2026-11-04): startup hitch re-bench (#102)
Bench world-features cfae8be (W-79 + W-79b, includes main with A-52) against 4e3b63c (job 19's capture), laptop player, uncapped, 3 cold starts on v2 and v3, no town teleport. Same capture-from-launch breakdown as job 19: the "island ready" frame ms and its parts (bake, flora, fauna, caves, first terrain batch, anything else over 200 ms), island ready and "[stream] world ready" times. The editor shows v3's island-ready frame at ~29 s with audit/biome reports in it: say whether the player runs those. Host + local client once: the client's flora rebuild ms. Landmark hashes (expect v2 B5EFF8C8, v3 3A0DC7BB). Read-only.

## Job 21 (2026-11-04): dungeon host/client check (#24, PR #104)
Main e31291e, laptop player build, island v3. Two processes: host + client joining it. Both walk the player into the temple (P-7) and the stand-in cave. Check: same spawner pick hash on both (temple 0x7c8c8b71, cave 0xa9995035 in editor), creatures visible on the client, DungeonPost count > 0 on the host a minute later, a guard log line and a drink log line, 0 exceptions, frame ms inside the temple. Read-only.

## Job 22 (2026-11-04): cave FPS at the bat folk cave (#81)
Main 0ec7a8d, laptop player build, uncapped. A-53 found editor main-thread CPU fine at the v2 bat folk cave (530, 958): 8.1 ms (v3's cave at -2674, -608: 5.2 ms). So measure the build: at both caves, frame ms, main vs render thread vs GPU ms, draw calls/batches, shadow casters, and the top GPU passes (cave fog, glow, particles, white pools). Compare with a surface spot nearby. Say which lane owns the biggest cost. Read-only.

## Job 23 (2026-11-07): process cleanup in shared scripts (profiler self-fix, b/fix-proc-cleanup)
Zach found hundreds of leftover bash/grep processes (rule 18 in COORDINATION.md). Small fixes to the shared scripts:
1. `scripts/run-playmode-tests.sh`: Unity runs without a timeout, so a hung test editor lives forever. Wrap it in `timeout 3600` and report exit 124 as "timed out".
2. `Tools/bench/smoke.sh`: `Stop-Process -Name Punchy` kills every Punchy.exe on the machine, including Zach's game and other lanes' runs. Keep `$!` from the `&` launch and kill only that PID (and its children).
3. `Tools/bench/lib.sh` `strays()`: kills every batch-mode Unity.exe and every bench Punchy.exe, so one lane's bench can kill another lane's batch build or test. Limit it to processes whose command line holds this tree's project path or its Builds folder.
4. `lib.sh` mp client loop (`( ... ) &` then `wait`): make sure the background client loop also ends when the host run times out, for example by wrapping it in `timeout 1000`.
Commit on b/fix-proc-cleanup and post the SHA here; the supervisor reviews and merges.
**Pushed 2026-11-07: b/fix-proc-cleanup 9ef5867e, for review. Report in REPORTS-profiler.md.**
**Reviewed and merged 2026-11-07 by the stand-in supervisor (PR #197). Done.**

## Refactor 1.2 (2026-11-07, REPORTED in REPORTS-profiler.md, b/bench-stress dd1d4738 for review; started by the profiler with Zach's OK): stress stops on v0.13.0 (4fea789)
New runner stops on b/bench-stress, used only when named: bigtown (the settlement with the most buildings), firesbase/fires (-benchfires N, default 500), plus forest, an under-canopy -benchspot and lake. These are measured at 1080p, with 1440p pixels from render scale 1.333 (the laptop desktop is 1920x1080, so bigger windows clamp). Results will go in REPORTS-profiler.md. Supervisor: queue new refactor jobs below this one, numbered from 24.

## Job 24 (2026-11-07): population sweep, refactor baseline (REFACTOR-ROADMAP.md 1.1)
The refactor needs to know how the creature pipeline scales before 2.2-2.5 are carded. Build main at exactly e5c88fb4
(it has the new `-wildcap N` test flag, PR #196; refactor branches merged after it would move the baseline), a Profile or Development build, at your fixed path. v0.13.0 is the
release baseline.
- **Runs:**
  - Wild cap N = 180 (today's default), 500, 1000 and 2000.
  - `-wildcap N` raises the cap and scales the spawn rate and the species densities. The log says `FaunaDirector: -wildcap ...`. Expect most of N alive within the 170 m despawn ring.
  - Same spot and settings every run: `-place meadow -time 12`, uncapped, `-gfx Low`.
  - Let the population fill for about 70 s, then sample about 20 s. Take two samples per N, or ABBA order if you see drift.
- **Report per N:**
  - live creatures
  - frame ms (mean and worst)
  - main-thread ms
  - brain job wall ms, and its worker spread
  - `brain wait` on the main thread
  - pose job ms
  - CreatureRenderSystem CPU ms, and GPU ms for creatures
  - the snapshot and the main-thread readers (audio, trace, mantrap, wake)
  - GC KB a frame (`-gclog`)
  - native memory
- **Where the numbers come from:** `-perflog` on a shot line prints most of them (perf / perf2 lines). Use SysMs for per-system ms and the render CPU/GPU split.
- **Then split it two ways:**
  1. Everything near: the default, everything inside the ring.
  2. Mostly far: the same N with most creatures outside the active-thinking LOD (brain LOD skips). Say how you arranged that if it needs a flag; ask in the report if none fits.
- **What the supervisor needs back:** the main-thread brain prep (snapshot arrays, lookup and grid build) as a share of the frame at each N, and which cost grows fastest (brain, pose/render, readers, snapshot). That decides the order of refactor items 2.2-2.5. Numbers only, in REPORTS-profiler.md.

## Job 25 (2026-11-07): stress scenes, refactor baseline (REFACTOR-ROADMAP.md 1.2)
Same build as job 24. For each scene, say whether it is CPU-bound or GPU-bound at 1080p and 1440p. Give frame ms, main
thread ms, GPU ms, draw calls and tris, and the top 5 systems by ms (SysMs).
1. **Big settlement:** the largest town you can find on v3 (`-place HumanVillage`, or whichever town logs the most kit buildings), standing in its middle.
2. **Dense forest:** a dense-forest spot (your pick; log the plant instance count in view).
3. **Fires:** about 500 fires at once. The Ember Forest's burning forest at night is the nearest thing. Say what count you reached and how (FireSystem caps flames at 8192).
4. **Lake:** a lake filling most of the screen. For example the W-120 sea folk lake at `-goto -1624,-488`, looking across it at noon; repeat with the camera under the water (`-view 0,-35`).
These feed refactor phase 3 (3.1 lights, 3.2 fire, 3.4 water, 3.5 flora by behaviour). For 3.5, also log the flora vertex count split into plants vs rocks/kit buildings in scene 1, if SysMs or a render counter can.

## Job 26 (2026-11-07): static flora variant A/B (REFACTOR-ROADMAP.md 3.5b), after jobs 24-25
Branch `refactor/flora-static` (5febffdb, not merged: merge waits on this job). Flora models that never move (rocks,
logs, ruins, kit buildings) draw with a FLORA_STATIC variant that skips all vertex wind and motion. The build log says
`[flora] static (refactor 3.5): 563 of 1088 variants never move, 694018 LOD0 vertices static vs 1622264 moving`.
`-nostaticflora` draws everything animated, as before, in the same build. That is the A/B.
- **Build:** a Profile build of the branch. Set the resolution explicitly every run: two runs here came out at 1080p and 4K from a saved screen pref, so set it per run (see build-release.md on the Screenmanager values).
- **Scenes:** the job 25 big settlement (expect the biggest win: kit buildings), the dense forest (mostly plants: little change expected), and a rock-heavy spot (`-place Tors`).
- **Per scene:** run ABBA with and without `-nostaticflora`, uncapped, at 1440p. Report GPU frame ms, and the shadow pass and main opaque pass ms if SysMs or a GPU capture can split them.
- **Also report** anything that looks different between the two. The player lane saw no difference at a town, the tors and an oak grove.

## Job 27 (2026-11-07): fire instance buffer A/B (REFACTOR-ROADMAP.md 3.2), after jobs 24-26
Branch `refactor/fire-instances` (03783ca9, not merged: merge waits on this job). FireSystem no longer rebuilds and
uploads a mesh every frame: one 48-byte instance per flame, and two procedural draws (near, far).
- **A/B:** a Profile build of the branch against one of main e5c88fb4, same spot, uncapped (`-fpscap 0`), explicit resolution, ABBA.
- **Scenes:**
  - The Ember Forest burning at night: `-place EmberForest: -time 22`, then `-view 120,5`. That has both near and far flames.
  - Job 25's 500-fire scene, if you found one.
- **Report:**
  - main-thread ms of LandmarkEffectSystem (FireSystem.Draw runs inside it)
  - GPU ms for the flame passes
  - frame ms
  - the flame count, and `FireSystem.Dropped` (the cap is 8192)
  - anything that looks different. The player lane saw none at the Ember Forest.
