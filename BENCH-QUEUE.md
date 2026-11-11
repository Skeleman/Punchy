# Bench queue (supervisor → kind-possum)

Since J-041 kind-possum runs on a different Claude account, so cross-session messages may not reach it.
The supervisor adds requests here; kind-possum pulls before each job and reports in BENCH-REPORTS.md
(newest first, one short entry per queue: gate results and real regressions only).

## Setup (once, after the account switch)
- Pull `coordination` and run `sh hooks/install.sh <fixed folder outside your trees>` (J-041 commit telemetry).
  Make one test commit on a b/ branch and check ~/.punchy-telemetry/commits.csv gets a row.

## Standing rule: never idle (Zach, 2026-11-10)
- When no job is QUEUED or RUNNING, take the next open part of job 47 (LOD range sweep), one kind per session, and report it like
  any job. Jobs the supervisor queues always go first; stop a sweep part at a clean point when one appears.

## Standing rule: all profiling is yours (Zach, 2026-11-10)
- Lanes on the main PC no longer time anything (COORDINATION rule 21). Every cost check comes here as a job; expect more of
  them, each naming a branch, commit, spots and arms. Branch-vs-main arms on the same build setup, as usual.

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

## Job 24 (2026-11-07, REPORTED in REPORTS-profiler.md; q52.sh, build 01cb543 = e5c88fb4 + scripts-only #197): population sweep, refactor baseline (REFACTOR-ROADMAP.md 1.1)
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

## Job 25 (2026-11-07, REPORTED in REPORTS-profiler.md; q53.sh, runner switches on b/bench-stress 472b629d for review): stress scenes, refactor baseline (REFACTOR-ROADMAP.md 1.2)
Same build as job 24. For each scene, say whether it is CPU-bound or GPU-bound at 1080p and 1440p. Give frame ms, main
thread ms, GPU ms, draw calls and tris, and the top 5 systems by ms (SysMs).
1. **Big settlement:** the largest town you can find on v3 (`-place HumanVillage`, or whichever town logs the most kit buildings), standing in its middle.
2. **Dense forest:** a dense-forest spot (your pick; log the plant instance count in view).
3. **Fires:** about 500 fires at once. The Ember Forest's burning forest at night is the nearest thing. Say what count you reached and how (FireSystem caps flames at 8192).
4. **Lake:** a lake filling most of the screen. For example the W-120 sea folk lake at `-goto -1624,-488`, looking across it at noon; repeat with the camera under the water (`-view 0,-35`).
These feed refactor phase 3 (3.1 lights, 3.2 fire, 3.4 water, 3.5 flora by behaviour). For 3.5, also log the flora vertex count split into plants vs rocks/kit buildings in scene 1, if SysMs or a render counter can.

## Job 26 (2026-11-07, REPORTED in REPORTS-profiler.md; q54.sh): static flora variant A/B (REFACTOR-ROADMAP.md 3.5b), after jobs 24-25
Branch `refactor/flora-static` (5febffdb, not merged: merge waits on this job). Flora models that never move (rocks,
logs, ruins, kit buildings) draw with a FLORA_STATIC variant that skips all vertex wind and motion. The build log says
`[flora] static (refactor 3.5): 563 of 1088 variants never move, 694018 LOD0 vertices static vs 1622264 moving`.
`-nostaticflora` draws everything animated, as before, in the same build. That is the A/B.
- **Build:** a Profile build of the branch. Set the resolution explicitly every run: two runs here came out at 1080p and 4K from a saved screen pref, so set it per run (see build-release.md on the Screenmanager values).
- **Scenes:** the job 25 big settlement (expect the biggest win: kit buildings), the dense forest (mostly plants: little change expected), and a rock-heavy spot (`-place Tors`).
- **Per scene:** run ABBA with and without `-nostaticflora`, uncapped, at 1440p. Report GPU frame ms, and the shadow pass and main opaque pass ms if SysMs or a GPU capture can split them.
- **Also report** anything that looks different between the two. The player lane saw no difference at a town, the tors and an oak grove.

## Job 27 (2026-11-07, REPORTED in REPORTS-profiler.md; q55.sh, q55b.sh): fire instance buffer A/B (REFACTOR-ROADMAP.md 3.2), after jobs 24-26
Branch `refactor/fire-budgets` (on top of `refactor/fire-instances` 03783ca9: also the Ember Forest near/far hand-off fix; not merged, merge waits on this job). FireSystem no longer rebuilds and
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

**Supervisor review 2026-11-07 (stand-in):** jobs 24-27 read. Job 27's branch merged (PR #201); job 26's branch not merged (no gain). b/bench-stress merged (PR #202). Queue empty until the next refactor A/B.

## Job 28 (2026-11-07, REPORTED in REPORTS-profiler.md; q56.sh, q56b.sh, runner b/bench-stress 5c97ef08 for review): static flora for bigger cities: grouping, bake ceiling, first-approach hitch, cache redraws (REFACTOR-ROADMAP.md 3.5)
Zach plans much bigger cities, so the stand-in is reconsidering the static variant (job 26 found no gain at today's
size). Branch `refactor/flora-static` (head pushed 11-07; main merged in), a Profile build, 1920x1080 set explicitly,
uncapped, ABBA.
- **A. Static variant with grouped draws:** draws are now grouped by material.
  - Big settlement and Tors, with and without `-nostaticflora`.
  - Report GPU ms, the shadow pass, SetPass calls, and the render-thread ms.
  - Has the grouping changed anything against job 26?
- **B. Bake ceiling:** the same spots, with and without `-nofloradetail`.
  - It skips the flora shader's procedural weathering, wood, cloth and strata detail.
  - The GPU difference is the most baking that detail could save.
  - Also run the rock-heavy Tors, where strata rocks use it. Plot buildings in towns don't run weathering at all (the player lane checked).
- **C. First-approach hitch:** start well outside a town, then walk or `-goto` into its middle.
  - Report the worst frame and any frame over 50 ms in the first seconds after arriving, with and without `-nostaticflora`.
  - Is there a first-use stall when the static variant is first drawn? If there is, the stand-in adds a shader warm-up.
- **D. Far shadow cache redraws while walking:**
  - In the big settlement, walk 100 m through it, using a route or repeated `-goto` steps.
  - Report the main thread and GPU ms of the frames that redraw the cache (FloraSystem.ShadowCache, every 10 m of movement, spread over CacheFrames), against frames that don't.
  - This sizes "scroll the cache instead of redrawing it".

## Job 29 (2026-11-07, REPORTED in REPORTS-profiler.md; q57.sh): where the lake's GPU time goes (REFACTOR-ROADMAP.md 3.4 water)
Job 25 measured the lake at 25 ms GPU from the shore and 38 ms with the camera under the water (1080p), the worst
scenes on the island. But earlier work at fishing village 1 found water itself cost about 0-2 ms there. Before any
water rewrite, split the lake's cost. Branch `refactor/water-costs` (head a21cb739; main plus one switch), a Profile
build, 1920x1080 set explicitly, `-gfx High`, `-weatherseed` fixed, uncapped, ABBA. Spots: the job 25 lake stop
(-1624,-488, noon) from the shore and under the water (`-benchpitch -35`).
- **Runs at each spot:**
  - Plain.
  - `-benchnoshader Water` (water off entirely; your bench switch).
  - `-waterssr 0` (new: skips the screen-space reflection march only).
  - `-waterdetail 0` (skips the two fine-ripple texture reads, caustics and glitter).
  - `-waterssr 0 -waterdetail 0` together.
- **Report** GPU ms, frame ms and render-thread ms for each, and the difference from plain.
- **If water off saves little:** say what fills the frame instead. The spot stands inside a sea-folk pillar (job 25
  note); a few metres away is fine if the pillar is the cost. A GPU capture of the shore view's biggest passes
  would help (opaque, transparent, shadows, post).
- **Under water:** also try `-benchnoshader` for the underwater fog/post if your bench has a switch for it.

## Job 30 (2026-11-08, REPORTED in REPORTS-profiler.md; q58.sh; sky follow-up queued): water layers skipped where unused, A/B; and the underwater frame without water (REFACTOR-ROADMAP.md 3.4)
Thanks for job 29. Branch `refactor/water-costs` head 346cf4d9 against a21cb739 (the job 29 build). The new commit
skips shader layers whose weight is zero (river flow on still water, rain when dry, thick-water swells and murky rings
on clear water, the toxic swirl on plain water). Screenshots match the old build (mean difference 0.04/255, the wave
clock). Same setup as job 29: Profile build, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, ABBA.
- **A. Lake shore and under** (job 29's spots): plain, old build against new. Report GPU and frame ms. The aim was
  part of the ~4.5 ms "rest of the water".
- **B. A river and the sea**, same A/B, so a flowing river (no change expected) and open sea (some change expected)
  are covered. Any river stop from the bench routes is fine.
- **C. The underwater view without water was still 29 ms GPU.** Split that, on the new build only:
  - `-benchnoshader` on the terrain shader, then the flora shader (water off in both).
  - The same view with the camera a few metres from the sea-folk pillar.
  - Say what's left.

## Job 31 (2026-11-08, REPORTED in REPORTS-profiler.md; q59.sh, q59r.sh): close-up shadow range, 25 / 50 / 75 m, static vs animated (REFACTOR-ROADMAP.md 3.5c)
Zach finds the hand-off from real-time to cached shadows unsatisfying and is weighing a wider close-up range. This
sizes what that costs and how much the static flora variant wins back once more casters are drawn every frame.
Branch `refactor/flora-static` (job 28's build is fine), Profile build, 1920x1080, `-gfx High`, `-weatherseed 4`,
uncapped, noon. `-shadownear <m>` already exists (FloraShadowOptions.NearRange, default 25; the far cache starts
there).
- **Spots:** the big settlement (job 28's bigtown) and a dense forest (job 25's).
- **Runs:** `-shadownear 25`, `50`, `75`, each with and without `-nostaticflora`. 2 rounds, order reversed.
- **Report:** GPU ms, the URP shadow pass ms (from a capture if the route can't split it), main and render thread ms,
  and shadow-caster draw counts if a capture gives them.
- **Shots:** one per range at the big settlement, looking along a street at a low sun (`-time 17`), so the hand-off
  line can be compared by eye.
- **Job 31 addendum (2026-11-08): RenderDoc is installed on the profiler laptop.** At each spot (big settlement and
  dense forest, `-shadownear 25`, static on), take 3 frame captures with RenderDoc (Launch Application on the Profile
  build, F12). For each, give GPU time per pass from the Event Browser's timings: depth prepass, main-light shadows
  (and how many draws), opaque terrain, opaque flora, creatures, skybox, opaque/depth copies, transparents (water,
  particles), post. Also one capture at job 29's lake spot under water. This is the first full split of the GPU
  frame; the D3D11 recorders can't give it.

## Job 32 (2026-11-08, REPORTED in REPORTS-profiler.md; q60.sh): what the sky costs, by weather; and Snell's window under water (REFACTOR-ROADMAP.md 3.4)
Job 30 left ~27 ms of the underwater frame on what looks like the sky. The sky shader ray-marches the volumetric
clouds per pixel (up to 160 steps, 4 light samples per step inside a cloud, an early out only once a cloud turns
opaque). Zach also wants clearer weather more often and asks whether fewer clouds would be cheaper. Profile builds,
1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, ABBA.
- **A. Sky cost by weather** (main 46012623): an open view with lots of sky, e.g. the bench route's hill or mesa top
  facing the horizon at noon (`-benchpitch -10` or so, the sky over half the screen). Runs:
  - Plain.
  - `-weather Clear`, `-weather Overcast`, `-weather Storm` (each forced over the player; the clouds overhead and
    out to ~15-20 km are what count).
  - `-benchnoshader Sky` (sky off).
  - Report GPU ms per run, and the difference from sky off. One RenderDoc capture each (plain, Clear, Overcast) with
    the skybox draw's GPU time.
- **B. Under water, Snell's window + sky skip** (`water/snells-window` head, against main 46012623): job 29's lake
  spot under water (`-benchpitch -35`) and 12 m east of the pillar. GPU and frame ms. The branch draws the surface's
  underside as Snell's window (no above-water lighting there) and returns the fog colour for sky pixels under water.
- **Job 32 addendum (grass density cost): withdrawn.** Zach tried `-grassdensity` before: more evenly scattered
  blades don't read as fuller. The player lane is prototyping blade clumps instead; that will get its own A/B.

## Job 33 (2026-11-08, REPORTED in REPORTS-profiler.md except water/river-flow, not pushed yet; q61.sh, q63.sh): what the overnight prototypes cost (Zach: "if any of the shaders wind up costly, let me know")
Each branch below is pushed separately; time each against main 46012623 (or say which main you used). Profile builds,
1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, ABBA. GPU ms, frame ms, main and render thread ms; flag anything
over +0.5 ms GPU.
- **`water/swells`** (sea swells, breakers, whitecaps, shore contact by kind; includes Snell's window): a beach looking
  out to open sea (the bench's Beach stop) in `-weather Clear` and `-weather Storm`, and with `-noswells` on the same
  build. Also job 29's lake shore view (lakes get the new still shore line only).
- **`water/lava-plates`** (lava drawn as cooled plates on a glowing melt, cellular pattern): `-place VolcanoCone`
  looking into the crater, lava filling as much of the screen as possible.
- **`refactor/pose-by-size`** (creature pose bands scale with body size, so huge animals are posed every frame further
  out): CPU, not GPU. A spot with a roc or sky serpent in view (any wild stop where one shows) and the busy wild stop
  from job 25 (`-wildcap 2000`): CreatureRenderSystem pose ms and the posed/reused counts, against main.
- **`night/moon-sky`**: no cost expected (two scaled colours); skip unless convenient.
- **Job 33 addendum (overnight):** also time
  - `water/river-flow`: the job 30 river stop and job 29's lake shore view, plain against `-waterrefraction 0` on the
    same build (the refraction adds a screen and a depth read per water pixel), and against main.
  - `water/glitter-grid`: job 29's lake shore view against main (expected no change: one read's UVs are turned).
- **Job 33 addendum 2:** `grass/clumps` (each grass instance a tuft of 5 blades, half as many roots, so ~2.5x the
  blades): `-place meadow` at noon looking across the field, against `-grassclumps 0` on the same build. GPU ms and,
  if a RenderDoc capture is handy, the grass draws' time.
- **Job 33 addendum 3:** `night/blue-moon` (blue streaks in the sky shader on blue-moon nights, a loop of 3 for sky
  pixels near the blue moon): `-bluemoondays 0 -time 0 -weather Clear`, camera on the blue moon, against main. Expected
  near zero; flag it if not.
- **Note for the Job 32 Summit freeze (player lane, 2026-11-08 ~07:15):** not reproduced outside the bench route. A
  Development build of main 46012623, `-goto -2446,1154 -time 12 -weatherseed 4`, 13 shots 5 s apart, twice (once
  `-fpscap 0` looking up at `-view 0,-10`): no stall, colossi built frames 40-56, every shot on time. So it needs
  something the route or the Profile build adds (the `-benchplace` jump and settle, `-benchweather`, the profiler
  connection, or BenchmarkRoute/BProfileTools themselves). To find the code: when it freezes, attach Visual Studio (or
  `procdump -ma <pid>`) and break, or run the same route on a Development build with "Script Debugging" so the main
  thread's managed stack can be read; a stack from inside the freeze will name it. The last log lines (MagmaSerpent made
  solid, `Weather: forced`, a JobTempAlloc leak warning) are just the last things that happened before it, not the cause.

## Job 34 (2026-11-08, REPORTED in REPORTS-profiler.md; q64.sh): the cloud march's empty-air strides (sky/cost)
Job 32's sky was 14.7-18.8 ms GPU at the Summit. On this branch the march strides through air that can't hold cloud:
steps per pixel fall from 140+ to about 55 (`-skyoff 32` draws the step bands). Use head 79ffa7c9 or later (the first version streaked cloud edges). A few extra far clouds show on the new path: rays now reach ones the old 160-step cap stopped short of. The desktop's GPU is too
fast to show it (2.9 -> 2.7 ms), so the laptop gives the real number. Branch `sky/cost` head against main 4669aabc,
Profile builds, job 32's setup (Summit, pitch up 10, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, noon, ABBA).
- `-skyoff 16` is the old even stepping on the same build; `-skyoff 0` the new. Plain, `-weather Clear`, Overcast,
  Storm, plus `-benchnosky` once for the baseline.
- Then the parts, in plain weather: `-skyoff 1` (no march), `2` (no far clouds), `4` (no sky colour), `8` (no weather
  volumes). They say what's left once the march is cheaper.
- A sunset view at the same spot, old against new, for banding at low light.

## Job 35 (2026-11-08, REPORTED in REPORTS-profiler.md; q65.sh): can the depth-normal prepass go? (render/ssao-source)
Job 31: the prepass was 33% of a forest frame and 22% of a town frame. Only SSAO uses it. Branch `render/ssao-source` adds
`-ssao normals|depth|after|off` (per shot line; `normals` is today's). The desktop is CPU-limited and can't tell them
apart. Profile build, job 31's forest and bigtown spots, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, noon, ABBA.
- GPU ms for each mode at each spot.
- One RenderDoc capture per mode at the forest: **does the depth-normal pass disappear** (`depth`: a depth-only prepass?
  `after` and `off`: none?), or does something else keep it alive? Say which pass remains and its share.
- A shot per mode at both spots, so the look can be judged (the player lane will compare them for Zach).

## Job 36 (2026-11-09, REPORTED in REPORTS-profiler.md; q66.sh): clouds on the low-resolution buffer (sky/lowres ccd74cc1)
This PC's numbers disagree between rounds, so the laptop gives the real one. Job 34's setup (Summit, pitched up 10, 1920x1080, `-gfx High`, `-weatherseed 4`, uncapped, noon, ABBA). `-skyfull` is today's full-resolution sky; `-lowres half` and `-lowres quarter` force a size. Report GPU ms for each against main, plus plain, Clear, Overcast and Storm at the default size, and shots of a tree line and the horizon for halos, and one storm view (weather volumes weren't shot).

## Job 37 (2026-11-09, REPORTED in REPORTS-profiler.md; q67.sh): R-2 on the laptop (main acd0cad5)
Build main acd0cad5 (R-2 merged as #218). (1) World glow bake time on the laptop (the `[glow]` bake line in the log; 4.1 s on the main PC). (2) The 3.2 fire test (Ember Forest, `-benchfires` 500 fires, as job 27): main thread and GPU ms, against 13cdbcdc (before R-2). (3) Ember Forest at night, `-worldglow 0` against default, GPU ms. ABBA, uncapped, 1920x1080, `-gfx High`. Report numbers in REPORTS-profiler.md.

## Job 38 (2026-11-09, REPORTED in REPORTS-profiler.md; q68.sh): why isn't quarter-resolution sky cheaper than half? (main acd0cad5)
Job 36: Summit 22.25 ms full, 9.0 half, 9.0 quarter. Quarter has 1/4 of half's pixels, so what's left must be a cost that
doesn't scale with resolution. Main acd0cad5 Profile build, job 34's setup (Summit, pitched up 10, 1920x1080, `-gfx High`,
`-weatherseed 4`, uncapped, noon, ABBA), plain weather.
- At `-lowres half` and `-lowres quarter`: GPU ms with `-skyoff 0`, `1` (no march), `2` (no far clouds), `4` (no sky colour),
  `8` (no weather volumes), and `-benchnosky` for the baseline. If a flag no longer exists, say so and skip it.
- One RenderDoc capture at quarter: list the sky's passes (march, upsample, composite, cloud shadow map, any full-resolution
  pass) with their GPU ms. Name the biggest pass that doesn't scale with resolution.
- Report the numbers in REPORTS-profiler.md as usual. The rendering lane uses this to decide what to cut.

## Job 39 (2026-11-09, REPORTED in REPORTS-profiler.md; q69.sh): depth priming on the laptop (main 5558f89a, R-3 #220)
R-3 row 4: on the desktop, forced depth priming beats off at every spot (forest prepass+opaques 1.61 vs 2.81 ms). The laptop's
iGPU may differ. Profile build of main 5558f89a, 1920x1080, `-gfx High` then `-gfx Low`, `-weatherseed 4`, `-time 12 -weather
Clear`, uncapped (`-fpscap 0`). At `-goto 2972,-885` (big town), `1740,3355` (forest), `-1624,-488` (lake), `-2446,1154`
(Summit): ABBA `-depthpriming forced` / `off`, 300 frames each. Report whole-frame GPU ms and the prepass and opaque pass ms
per spot and preset. If `off` wins at Low, say by how much: the Low renderer can then turn priming off.

## Job 40 (2026-11-09, REPORTED in REPORTS-profiler.md; q70.sh): R-4 sky cost on the laptop (sky/look 5880293c vs main 5558f89a)
R-4 row 4 compiles the full-resolution cloud march out of the skybox while the low-res cloud buffer is on (job 38 named
DrawSkybox at 3.7 ms, same at half and quarter). The main PC's GPU is too fast to resolve it. Profile builds of both, job 34's
setup (Summit `-goto -2446,1154`, pitched up 10, 1920x1080, `-gfx High`, `-weatherseed 4`, `-time 12`, uncapped `-fpscap 0`),
ABBA, 300 frames each.
- Plain weather at `-lowres half` and `-lowres quarter`: whole-frame GPU ms for both builds, plus R-4 with `-skyoff 896`
  (main's cloud look on R-4 code) and `-benchnosky` as the baseline.
- `-weather Overcast` and `-weather Rain` at half, both builds (the new stratus sheets).
- One RenderDoc capture of R-4 at half: DrawSkybox ms against job 38's 3.68.
- Pass: R-4 within +1 ms of main at each spot. Say whether DrawSkybox dropped. Report in REPORTS-profiler.md as usual.

## Job 41 (2026-11-09, REPORTED in REPORTS-profiler.md; q71.sh; queued by the main-account supervisor): what the Summit stall waits on (jobs 37-38)
A read-only code review (supervisor, 11-09) found no unbounded shader loop and no main-thread GPU readback during settle.
`-skyoff` only sets a float; in q68 the -skyoff 2/4 games all sat at positions 5-8 of each round, so the link is likely timing,
not the flag. Ranked suspects: (1) the main thread blocked in the render thread or the Intel D3D12 driver (Present, swapchain or
fence wait), which fits 0% GPU, idle clocks, and stalls that end on their own; (2) thread-pool starvation behind
FloraGenerator.TakePregrown's `task.Result` on the main thread (hundreds of Task.Run grow jobs plus Parallel.For bakes); (3) the
runner (unlikely since b/fix-stall-hang).
- Main 0e964452 (or later), Profile build, job 38's Summit setup, `-lowres half`, plain weather. 24 games, each a fresh start with
  the Summit jump, game order shuffled per round. Half the games `-force-d3d11`, half default D3D12 (interleaved).
- On each stall: log the main thread's wait reason and the slow-polling thread's wait reason, plus each one's start address as a
  module + offset (UnityPlayer, d3d12, igd*, mono or coreclr), from the process's thread list. If you have a dump tool already,
  one minidump of a stalled game is welcome; don't install anything new without Zach's OK.
- Report: stalls per API (D3D11 vs D3D12), and for each stall which module the two threads sit in. If the main thread waits in
  Present or a fence: driver/render-thread side (I'll card a frame-latency or swapchain change). If it waits in a managed Monitor
  or Task wait: TakePregrown (I'll card the timeout fix). If too few stalls happen to tell, say so and stop at 24 games.

## Job 42 (2026-11-09, REPORTED in REPORTS-profiler.md; q72.sh; queued by the main-account supervisor): fix the Summit stall's swapchain wait (job 41)
Job 41 caught the main thread in `WaitOnSwapChain` (WaitForLastPresentationAndGetTimestamp) on both D3D11 and D3D12, with the GPU
idle, and any cdb attach wakes it: a missed wakeup on the waitable swapchain. Under the profiler self-fix rule, branch
`b/fix-swapchain-stall` from main, and add start-up flags only (set from script at runtime; no ProjectSettings change):
`-maxqueued N` (QualitySettings.maxQueuedFrames) and `-vsync N` (QualitySettings.vSyncCount). Then job 41's setup, 24 games
per arm, shuffled, D3D11 default, until the first arm that is clearly better or all arms ran:
- A: no flags (control). B: `-maxqueued 1`. C: `-maxqueued 3`. D: `-vsync 1`. E: `-force-d3d11-no-singlethreaded`.
- Count stalls of 30 s or more per arm, and fps at the Summit stop when not stalled. Use cdb only to confirm a stall's stack, not
  to end it: leave each stall up to 120 s, then kill the game by PID.
- Pass: an arm with 0 stalls in 24 games that costs at most 3% fps. If one passes, make it the default in the branch (still from
  script, behind a flag to turn it off), commit, push, and hand back on REPORTS-profiler.md; I review and merge. If none passes,
  report the counts and stop: the next step would be a present-wait timeout card for the rendering lane.
- Fix the runcsv `i` reuse bug from job 41 first, so per-game labels and logs are right.

## Job 43 (2026-11-09, REPORTED in REPORTS-profiler.md; q73.sh; queued by the main-account supervisor): water, SSAO, glow slots and shadow distance on the laptop
From the 11-09 outside audit (docs/Chat audit 11-9.md, sections 3, 4, 5, 8). Read only, main at the time you start (after R-6
#227), Profile build, 1920x1080, `-gfx High` then `-gfx Medium`, `-weatherseed 4`, `-time 12 -weather Clear`, uncapped, ABBA,
300 frames each. If a flag below doesn't exist, say so and skip it; don't add code.
- Water: a spot where water fills about 10%, 50% and 90% of the screen (sea from a cliff, pitched down to fill it; pick spots
  and give their `-goto`). Per spot: whole-frame GPU ms and the water pass ms with water quality High, Medium, Low.
- SSAO: one forest spot and one town spot, SSAO on vs off, GPU ms of its passes.
- Glow slots: the big town at 22:00, glow slots on vs off (or forced 0), whole-frame GPU ms and the opaque pass ms.
- Shadow distance: forest and big town at noon, the current 50 m cascade distance vs 80 m and 120 m, if a run-time flag can set
  it; GPU ms of the shadow passes.
- One RenderDoc capture of the 90% water spot at High: list the water passes with GPU ms.
Report in REPORTS-profiler.md as usual: which of these costs more than 1 ms, and by how much.

## Job 44 (2026-11-10, REPORTED in REPORTS-profiler.md; q74.sh; queued by the main-account supervisor): W-129 streaming spikes in a player build
W-129 (REPORTS-world.md) saw frames over 100 ms in the editor but most were whole-editor stalls under host load (3 editors,
shader compiles). Read only, main at the time you start, Profile build, 1920x1080, `-gfx High`, uncapped, other editors closed.
- The shrub walk (z=1324, 1400 m at 30 m/s) 3 times and the glide (z=-1200, 6 km at 60 m/s) 2 times, each a fresh launch.
- Per run: mean ms, max ms with position, count of frames over 50 and 100 ms; for each frame over 100 ms, the top 3 samples
  (TerrainStreamingSystem and FloraSystem.LateUpdate especially, first visits).
- Report: does any streaming system repeat over 100 ms outside the editor? If yes, which and where; if no, say W-129 is closed.

## Job 45 (2026-11-10, REPORTED in REPORTS-profiler.md; q75.sh; 45b REPORTED, q76.sh; queued by the main-account supervisor): R-9 water field and R-10 lights on the laptop
Two Profile builds (one at a time), 1920x1080, `-gfx High`, uncapped, ABBA, 300 frames each arm unless noted. Read only.
- R-9: build branch `water/deform` (fd50ad58 or later). `-time 12 -weather Clear`, `-goto 488,-1144`, swim straight 10 s; then
  the same with `-waterfield 0`. Whole-frame GPU ms and the water pass ms, ABAB 2 runs each. Pass: on costs < +0.3 ms.
- R-10: build branch `light/forwardplus` at 0a4b3b50 or later (row 4b: dome off by default, town light sprites; add `-townglow 0` as a fourth arm). `-time 22 -weather Clear`, `-goto 2988.8,-920` facing north.
  Whole-frame GPU ms with `-lightbudget 0` vs default (48 at High) vs `-lightbudget 64` (the flag clamps at 64). Then the same spot on a build of
  origin/main as the "today" baseline. Pass: default <= +0.5 ms over main.
Report both in REPORTS-profiler.md with the pass/fail line for each.
- Update 11-10 (supervisor): R-10 row 4c is pushed, `light/forwardplus` 8263a9b5 (far lights: 7.8k far points from every night light, one draw).
  If the R-10 build isn't made yet, build 8263a9b5 instead. If it is, finish that and run 8263a9b5 as job 45b afterwards: same spot and arms,
  plus the same spot facing the Ember Forest. Pass: default <= +0.5 ms over main.

## Job 46 (2026-11-10, ON HOLD until W-132 rows 2-4 are pushed; the supervisor flips this to QUEUED; queued by the main-account supervisor): far ground variants on the laptop
Zach 11-10 asked for the far ground to cost a little less, and for the profiler to check it. Read only. Profile builds, 1920x1080,
uncapped, ABBA, 300 frames each arm, `-weatherseed 4 -time 18.5 -weather Clear`, at `-gfx High` then `-gfx Ultra`.
- Arms: origin/main (today's far ring), `world/far-lite` at the commit the supervisor names, and that branch with each row's off
  switch (the W-132 report lists them).
- Spots: W-131's walk (-1236, 1324), its hilltop, and 300 m up flying 60 s at 40 m/s.
- Per arm: whole-frame GPU ms, terrain + water pass ms, draw count, frames over 33 ms on the flight.
Report in REPORTS-profiler.md: saving per row in ms, and whether the cheap far path is worth keeping on the laptop.

## Job 47 (2026-11-10, STANDING: run when the queue is empty, one part per session; queued by the main-account supervisor): what LOD ranges can the laptop afford?
Zach 11-10: "if nothing in queue, [the profiler] should get tasks to attempt to check impact of adjusting LOD ranges to see what
acceptable ranges are." All visual LOD now goes by DetailPolicy.ScreenSize pixel thresholds (rule 20), so a range is a pixel
threshold: halving the threshold doubles the reach. Read only, on origin/main at the time you start (name the commit).
Method per part: Profile build, 1920x1080, uncapped, `-weatherseed 4 -time 12 -weather Clear`, `-gfx High` then `-gfx Medium`, ABBA,
300 frames per arm, spots: W-131's walk (-1236, 1324), its hilltop, 300 m flight 60 s at 40 m/s. Arms: reach x0.5, x0.75, x1 (today),
x1.5, x2. Record whole-frame CPU and GPU ms, draws, frames over 33 ms on the flight, and one shot per arm at the hilltop (so the
supervisor can see what the reach buys). If a kind has no override flag, add a bench-only `-lodscale <kind>=<x>` on `b/bench-lod`
(profiler self-fix rule; it only multiplies that kind's thresholds; the supervisor reviews and merges).
Answer per kind: the largest reach whose cost stays within +0.3 ms GPU and adds no 33 ms frames at High, and the same for Medium.
Parts, in order (mark each DONE here with the commit):
- 47a trees (FloraFloor, impostor hand-off; R-11 30 px) RUNNING on the laptop, q77.sh (main 33db029c + b/bench-lod 9bae987e -lodscale)
- 47b shrubs (R-7 impostors, reach 520 m today)
- 47c grass and pebbles (GrassEndPixels, PebbleEndPixels)
- 47d creatures (geometry and animation tiers, CreatureThinkPixels is gameplay: leave it)
- 47e shadow tiers (sharp / normal / wide)
- 47f landmarks and colossi (LandmarkLod)
- 47g far terrain (FarFromView, FarScale; after W-132 merges, use its knobs)
- 47h lights (after R-10 merges: far light tiers)
- 47i caves (ACCEPTED 11-10, supervisor): after W-135 rows land; inside a cave, at a mouth, on the surface over a network
- 47j+ (11-10): one part per kind as R-13 / W-135 / A-99 convert it to DetailPolicy.ScreenSize, same arms and +0.3 ms GPU bar
- 11-10 supervisor note: thanks for the 15:45 audit and for recording Zach's direction. It is now in cards: R-9 rows 4-6 (field cut only in its square + leading swell, waterfall cut, whitewater), W-134 (cave water = surface water, underground lake, river chamber), W-135 (cave LOD), A-99 (creature extras), R-13 (the rest). Job 45c (R-9 row 4 re-run, plus a fall arm after row 5) will be QUEUED when row 4 pushes. If Zach gives design direction here again, record it as you did; the supervisor picks it up from this file.

- Supervisor 11-10: main player build fixed by #248 (main 33db029c). Builds from main compile again.

## Job 45c (2026-11-10, REPORTED in REPORTS-profiler.md; q79.sh; queued by the world lane for R-9 row 4, #232): R-9 water field re-run on the laptop
Job 45 failed (+3.7 ms GPU, +4.7 ms CPU; the lake's water went 23,844 -> 319,872 indices). Row 4 now cuts the fine sheet by screen
size only near the player and leaves the rest to the chunk water: at the same lake, High, the sheet is 8,064 indices over the
chunk's 23,844 (chunk + sheet 1.34x the chunk alone). Read only.
- Branch `water/deform` at 24459300 (row 4 295063da plus origin/main with the #248 build fix). Profile build, 1920x1080, uncapped.
- Spot: job 45's, `-time 12 -weather Clear -goto 488,-1144`, swim straight 10 s (as q75.sh did), `-gfx High`.
- Arms: field on (default) vs `-waterfield 0`. ABBA, 300 frames per arm.
- Record whole-frame GPU and CPU ms and the water pass ms per arm.
Pass: on <= +0.3 ms GPU over off. A fall arm (fall 74, `-goto -2167,3,3691`) follows as 45d once row 5 pushes.

## Job 45d (2026-11-10, REPORTED in REPORTS-profiler.md; q80.sh d; queued by the world lane for R-9 row 5, #232): R-9 fall arm on the laptop
Row 5 adds a fine sheet on the river fall that looks biggest (cut along the drop, by screen size, under a 32k x Detail index
budget) with its own lip curl, curtain ripples and foot churn in the vertex shader. Fall 74 near: 45,240 sheet indices (High). Read only.
- Branch `water/deform` at 2856a634 (row 4 plus row 5; it can replace 24459300 for 45c too). Profile build, 1920x1080, uncapped, `-gfx High`.
- Spot: `-time 12 -weather Clear -goto -2167,3,3691`, then `-shotcam -2150.7,30.1,3675.1,-2180.2,12.1,3648.1` (fall 74 from 40 m, sheet fully on), hold 10 s.
- Arms: field on (default) vs `-waterfield 0`. ABBA, 300 frames per arm.
- Record whole-frame GPU and CPU ms and the water pass ms per arm.
Pass: on <= +0.3 ms GPU over off, CPU within noise.

## Job 45f (2026-11-10, REPORTED in REPORTS-profiler.md; q80.sh f; queued by the world lane for R-9 row 6, #232): R-9 rapids whitewater on the laptop
Row 6 adds standing waves and crest whitewater on rapids (3 sines per pixel on rapids, plus the mesh on the fine sheet), and reads the
river's flow ripples and streaks about a camera anchor (one extra read of each only while the camera is within 10 m of a 64 m grid line;
four at a corner). Shader only; index counts unchanged (sheet 7992, chunk 4944 at spawn).
- Arms: branch `water/deform` at 9b1cacc1 (row 6) vs 2856a634 (row 5, the 45d build). Profile builds, 1920x1080, uncapped, `-gfx High`, ABBA, 300 frames per arm.
- Spot: `-time 12 -weather Clear -goto -2167,3,3691`, then `-shotcam -2169,13.1,3682,-2184,3.1,3656` (the rapids below fall 74 from 30 m), hold 10 s.
- Second view: `-shotcam -2170,13.1,3682,-2185,3.1,3656` (6 m from the grid line x = -2176, inside the 3-10 m handover band), to price the double read.
- Record whole-frame GPU and CPU ms and the water pass ms per arm.
Pass: row 6 <= +0.2 ms GPU over row 5 at both views, CPU within noise.

## Job 48 (2026-11-10, REPORTED 11-11 (PASS; tundra 0.85 ms faster); q85.sh; queued by the main-account supervisor for A-99, merged as #250): creature extras by screen size on the laptop
- Arms: origin/main be98cde1 (before) vs origin/main 739cdff7 (after; A-99 branch fauna/extras-screen-size c0f22efe).
- Spots: `-goto -1500,-995` (hive), `-place HumanVillage -time 23` (night torches and lanterns), `-place Tundra` (traces and breath), `-goto -1149,1219` (bat roost). High, uncapped.
- Pass: frame ms within noise of the before arm at every spot. A miss means a fix branch (b/fix-*), as usual.

## Job 49 (2026-11-10, RUNNING 11-11 on the laptop (q87.sh); queued by the main-account supervisor for R-12, merged as #251): cloud warp and rain on the laptop
- Arms: origin/main 60edfacb with `-skyoff 1024` (warp off) vs plain; then 60edfacb vs 739cdff7 (before R-12). Main builds again since #248.
- Spots: (a) `-goto 1489,-897`, Storm at (5489,-897), 22:00 and noon; (c) ground under the storm's shadow with creatures in view: shotcam 1489,30,-897 -> 1889,25,-600, Storm at (1700,-1900), noon; plus the lone-oak spot in seed 4 rain (#247).
- What changed: one extra 3D noise sample per cloud-shadow and sun-cover call (per pixel in Creature, Cave, Colossus, mist shaders), one per rain step; the haze table now draws one pass instead of two.
- Pass: <= +0.1 ms GPU at every spot vs before. A miss means a fix branch.

## Job 45e (2026-11-10, QUEUED after 49; queued by the world lane for the R-10 merge): R-10 after merging main, on the laptop
- Arms: branch `light/forwardplus` at 1f6c966d (merge of origin/main 60edfacb) vs origin/main 60edfacb. Profile builds, 1920x1080, `-gfx High`, uncapped, ABBA, 300 frames per arm.
- Spot and views: job 45b's, `-time 22 -weather Clear -goto 2988.8,-920` facing north, then the same spot facing the Ember Forest. Default light budget.
- Record whole-frame GPU and CPU ms per arm.
- Pass: branch default <= +0.5 ms over main at both views. A miss blocks the R-10 merge.

## Job 50 (2026-11-10, QUEUED after 45e; queued by the main-account supervisor, Zach asked): two-player crash and desync test on the laptop
Note (supervisor 11-10): leave/rejoin is expected to crash in GameAudio occlusion until #259 is fixed. If it does, report it as #259 and finish the rest of the route without the rejoin step.
- Build: origin/main at the time you start (cc835cf6 or newer; note the commit), one Dev build, two instances on the laptop: host `-host`, client `-join 127.0.0.1`, start the client once the host is in game. Correctness only, no timing: two instances on one machine make frame times meaningless.
- Route (both players together, host leading): spawn; R-9 water (the rapids below fall 74 and the fall itself, then swim); a town at 23:00 (talk to a folk, A-93); a v3 cave and the bat roost; the storm spot `-goto 1489,-897` in rain; then the client leaves and rejoins once, and finally leaves for good.
- At each stop, from both logs and a shot from each side: both players see each other in the same place; same creatures (count and a few IDs and positions); same time of day and weather; same water and rain look; gear and clothing show on the other player (P-28); same world check hash in both logs.
- Report: exceptions, hangs, crashes and disconnects in host.log and client.log; every mismatch between the two sides with the spot; side-by-side shots at each stop.
- Pass: 0 exceptions, 0 crashes, the rejoin works, no mismatches. Stop at the first crash (crash rule) and report it.
- **Arm 50b (waits on P-31 merging):** after the plain run passes, repeat the same route with the client launched `-netsim 150,30,2` (150 ms delay, 30 ms jitter, 2% loss). Same checks; also note rubber-banding, snapping, late or lost gear swaps and talk prompts. Pass: 0 exceptions, 0 crashes, the rejoin works, the two sides agree once things settle.

## Job 51 (2026-11-10, QUEUED after 50; queued by the release subagent for R-13 row 2): visual ranges by screen size on the laptop
Row 2 turns the fixed-metre visual ranges (landmark effects, flames, chimneys, falls, forest fire crowns, beacons, colossus lanterns, lightning, basins, items, surface detail, driftwood, tree sway, room culling, ambient particles) and the shader fades (flora, terrain surface, water ripples, landmark effects) into DetailPolicy screen size. At the reference view (1080 lines, 60 degrees, Detail 1) the distances should be the same, so the cost should be too; at Medium (lower Detail) the Detail-scaled kinds now shrink with it where some did not before (lanterns, items, rooms).
- Arms: branch `lod/screen-size` at d9634f37 or newer (not 2bcbbba9: its DetailPolicy failed the Burst compile) vs origin/main 0477dd07. Profile builds, 1920x1080, uncapped, ABBA, 300 frames per arm, at `-gfx High` and again at `-gfx Medium`.
- Spots: (a) village at night `-place HumanVillage -time 22 -weather Clear`; (b) forest at night `-place meadow -time 23 -weather Clear`; (c) hilltop `-place HumanVillage -time 12 -frame HumanVillage -framing aerial`; (d) a 300 m flight from (c) at walking-camera height, timed by frame ms only.
- Record whole-frame GPU and CPU ms per arm and spot.
- Pass: branch within noise of main at High at every spot; at Medium, branch <= main. A miss means a fix on lod/screen-size before the merge.

## Job 52 (2026-11-10, QUEUED after 51; supervisor, for W-135, all four rows; folds in 47i): cave LOD and seam darkening
W-135 on `world/cave-lod`: row 1 skips hidden cave work (`-nocaveskip` turns it off), row 2 builds cave walls at 0.5/1/2 m by screen size with border skirts (`-cavelevel 0` = main), row 3 moves cave plants, drips, fish, falls and glow to screen size, row 4 bakes contact darkening into cave chunks (no shader change).
- Arms: (A) origin/main 0477dd07 or newer; (B) `world/cave-lod` at ef191d54 or newer, default; (C) B with `-cavelevel 0 -nocaveskip`. Profile builds, 1920x1080, High, uncapped, ABBA (A/B), then B vs C, 300 frames per arm.
- Views (shotcam):
  - (a) c5 tunnel (-2864, 24.5, -567) looking at (-2799, 27, -551)
  - (b) c5 big room (-2810, 27, -556) looking at (-2795, 29, -549)
  - (c) c1 mouth approach (-3155, 172, 1038) looking at (-3110, 169, 1038)
  - (d) room (-1389.2, 65.9, 1043.96) looking at (-1392.3, 63.4, 1037.4)
  - (e) ruin (1363.4, 9.85, -2544.1) looking at (1372, 7.3, -2544)
  - (f) crystal (1538.9, 3.96, -2596.45) looking at (1536.7, 1.9, -2591.95)
  - (g) surface over a cave network, open ground near (c)
- Also: one walk down the c5 tunnel per arm, and the first 20 s after load at (d); list frames over 2x the median (level swaps and contact rebuilds upload then).
- Record whole-frame GPU, CPU and render-thread ms per arm and view.
- Pass: B no slower than A on GPU and render thread at every view; B no slower than C; no new hitches on the walk or in the first 20 s.

## Job 53 (2026-11-10, CANCELLED: W-137 row 2b shelved, tunnels move to W-138; skip it; supervisor, for W-137 row 2b): tunnel portal covered ways on the laptop
W-137 row 2b on `world/mouth-rim`: each road-tunnel portal now builds a stone covered way behind the headwall (walls, stepped lintel roof, risers, end wall) plus wing walls. All 6 portals went from 31,968 to 88,176 vertices (15,984 to 44,088 triangles); editor build 7-31 ms per portal.
- Arms: (A) origin/main cc835cf6 or newer; (B) `world/mouth-rim` at b5312063 or newer. Profile builds, 1920x1080, High, uncapped, ABBA, 300 frames per arm.
- Views (shotcam, look along the road into each portal): (a) portal #4 (-2024.9, 261.4, 697.7); (b) portal #2 (-1878.9, 125.4, 97.7); (c) portal #0 (2045.7, 6.8, -1540.3); also one view at Medium for (c).
- Hitch check: approach each portal from 300 m at run speed per arm; list frames over 2x the median (the kit builds on first load).
- Record whole-frame GPU, CPU and render-thread ms per arm and view.
- Pass: B within noise of A on GPU and render thread at every view; no new hitch over 2x median on approach.

## Job 54 (2026-11-10, QUEUED after 52; supervisor, for W-136 rows 1-2): ground contact darkening on the laptop
W-136 on `world/ground-contact-shade` (builds on W-135): terrain chunks bake darkening under trunks, rocks, buildings and props. Per LOD0 chunk, a Burst job loops over the feet within 36 m (natural) or 52 m (built); loaded terrain re-meshes once when a chunk publishes.
- Arms: (A) origin/main 0477dd07 or newer; (B) `world/ground-contact-shade` at 3b1cb6aa or newer. Profile builds, 1920x1080, High, uncapped, ABBA, 300 frames per arm.
- Views: (a) big town `-goto 2972,-885` at 14:00, clear; (b) forest `-goto 1740,3355` at 14:00, clear; (c) village `-place HumanVillage -time 12 -weather Clear`.
- Also: the first 20 s after load at (a), and a 300 m run into (b) at run speed; list frames over 2x the median. If the build logs per-chunk build times, report the median and p95 ms per LOD0 chunk per arm.
- Record whole-frame GPU, CPU and render-thread ms per arm and view.
- Pass: B within noise of A on every view (no shader change, so GPU should match); no new hitch over 2x median on load or the run.

## Job 55 (2026-11-11, QUEUED after 54; supervisor, for W-134 row 1): cave water through the one ground query
W-134 row 1 on `world/cave-water`: SampleGround(float3) answers cave pools and streams below the terrain surface (one compare on the surface, then a per-cave box test); wakes, the R-9 field, swimming and river current now query with 3D positions.
- Arms: (A) origin/main 393a9c20 or newer; (B) `world/cave-water` at 0a726943 or newer. Profile builds, 1920x1080, High, uncapped, ABBA, 300 frames per arm.
- Views: (a) `-place cavepool`, the player swimming/wading in the pool; (b) a surface river, the R-9 rapids below fall 74; (c) a lake swim, `-goto 488,-1144` at y 9.2 (job 45's).
- Record whole-frame CPU and main-thread ms, plus WaterPatchSystem, CreatureWakeSystem and PlayerMovementSystem from the profiler markers.
- Pass: B within noise of A at (b) and (c); at (a) those three systems together no more than +0.1 ms CPU.

## Job 56 (2026-11-11, QUEUED after 55; supervisor, for W-132 rows 1-4; widened 11-11): cheap far terrain and sea shader, far disc, horizon skirt
W-132 row 2 on `world/far-lite`: far LOD chunks use runtime material copies with `_PUNCHY_FAR` / `_PUNCHY_FAR_WATER` (terrain 5856 -> 773 fragment instructions, water 9589 -> 1737; vertex instructions go up; shader variants double).
- Arms: (A) origin/main 393a9c20 or newer; (B) `world/far-lite` at b0a46b6d or newer. Profile builds, 1920x1080, High and Ultra, uncapped, ABBA, 300 frames per arm.
- Views: (a) ground walk in open country; (b) hilltop `-goto -3460,2425` looking west at 18:00, clear; (c) a 300 m-high flight over land, then over open sea.
- Also: first 20 s after load at (b); list frames over 2x the median (shader variant warm-up).
- Record whole-frame GPU, CPU and render-thread ms per arm, view and quality.
- Pass: B faster than A on GPU at (b) and (c), within noise at (a); no new hitch over 2x median.
- 11-11 widening: arm B is now `world/far-lite` at 0900515f (rows 2-4: far material copies, row 3 far disc on far-tile lines, row 4 horizon skirt of 1024 m tiles to ~6.2 km, HazeDistance 4600 m, analytic sun road on far water). Branch counts: ~340-420 fewer draws than main.
- Extra leg on the B build only: PUNCHY_FARSKIRT=0 vs default, and PUNCHY_FARDISC=0 vs 1, at (c) at 18:00 and at noon; record GPU/CPU ms, draws, and the Water and Skybox pass times (the sun road lives there). Pass: the skirt costs under 0.3 ms GPU at High.

## Job 57 (2026-11-11, QUEUED after 56; supervisor, for W-134 row 2): baked cave water
W-134 row 2 on `world/cave-water`: cave pools and streams are baked as 0.5 m quads fitted to the carved floor (a Burst bake job per cave when it first comes within LoadRadius), tiled, each tile drawn only while big enough on screen. Pool 2: 540 -> 2939 triangles; pool 8: 1040 -> 4706.
- Arms: (A) origin/main 393a9c20 or newer; (B) `world/cave-water` at 54dd9ff3 or newer. Profile builds, 1920x1080, High, uncapped, ABBA, 300 frames per arm. Can share a session with job 55 (same branch).
- Views: (a) `-place cavepool`, looking over the pool; (b) a stream cave if one can be placed; (c) the walk from the cave mouth to the pool (the bake fires on approach): list frames over 2x the median.
- Record whole-frame GPU, CPU and render-thread ms, plus draw calls and the bake job's main-thread cost if it shows in the profiler.
- 11-11 widening (W-134 row 3, 11176d1c): arm B at 11176d1c or newer. Also (d) inside lake 1's hall (-2600,205,334), standing and swimming; (e) the cave bake's new 'lakes' and 'lake details' steps, main vs branch.
- Pass: B within noise of A at (a) and (b); no new hitch over 2x median on (c).

## Job 58 (2026-11-11, QUEUED after 57; supervisor, for W-136 row 3 + W-135): one contact-shade path, caves and overworld
W-136 row 3 on `world/ground-contact-shade` (077acd90): one ContactShade path for terrain chunks, cave chunks and room/kit tiles; cave plants now use fitted feet (1209 -> 4564 contacts). Cost only, no look change.
- Arms: (A) origin/main 393a9c20 or newer; (B) `world/ground-contact-shade` at 077acd90 or newer. Profile builds, 1920x1080, High, uncapped, ABBA, 300 frames per arm. Can fold in job 54 (same branch, earlier rows).
- Measure: main-thread and job cost of (1) a ruin room and a lake room mesh build (`-place cavepool` and a ruin), (2) cave chunk mesh builds on the walk into a planted cave, (3) terrain LOD0 chunk builds over a forest and a town; plus whole-frame GPU/CPU ms at each spot and frames over 2x the median on the walk.
- Pass: no build more than 10% slower than A, no new hitch over 2x median.

## Job 59 (2026-11-11, QUEUED after 58; supervisor, for W-138 row 2a): first load near the sea folk landing
W-138 row 2a on `world/mountain-tunnels` (3e3c9e08): six new road spurs (+617 streets, +3.25 km), small-crest tunnels became cuttings, and the sea folk spots are found before the roads.
- Arms: (A) origin/main 393a9c20 or newer; (B) `world/mountain-tunnels` at 3e3c9e08 or newer. Profile builds, 1920x1080, High, uncapped, ABBA.
- View: shotcam near (-1803,50,-543) looking at (-1778,34,-553), the landing spur's end at the pier.
- Record: world-up time; the first 20 s after load, with frames over 2x the median listed; then whole-frame GPU, CPU and render-thread ms over 300 frames.
- Pass: B within noise of A; no new hitch over 2x the median.
