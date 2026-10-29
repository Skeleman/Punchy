# Handoff: profiler, kind-possum (after v0.8.0)

Written 2026-10-29 by the outgoing profiler session, for the fresh one. The machine is the laptop:
- i7-7700HQ (4 cores, 8 threads), Optimus GTX.
- Main checkout: `C:\Users\zache\Projects\Punchy`. My worktree: `C:\Users\zache\Projects\Punchy-wt`.
- Unity 6000.5.8f1 (the 6000.5.11f1 installer is in `C:\Users\zache\Downloads\unity-6000.5.11f1`, not installed: it needs UAC, which only Zach approves; the update is shelved).

Read COORDINATION.md first. This note covers only the profiler lane.

## Your next queue (supervisor, 2026-10-29)

1. **The runner.**
   - It's on `b/bench-runner` under `Tools/bench/`; most of it is written and partly tested (see below).
   - Finish it, then use it for the lane batch smoke benches that follow v0.8.0 (rule 12c).
   - Add the cap 60 pair for 4e3e4c1 / ba87e14 to the record.
2. **The audit.** Read-only. A ranked list of risky spots per lane, not an essay. For each: file:line, why it's risky, how it scales, the fix, the owning lane. The checklist:
   - Per-cell or per-player main-thread setup that should be Burst jobs. Tonight's two blockers were this class: FaunaDirector critter spots (03960d1) and AmbientLife.Settle (194a57d).
   - EntityQuery created per frame. AmbientLife.Near and my BenchmarkRoute do it; there are probably more.
   - GetComponentData / Lookup misuse in loops.
   - MonoBehaviours doing world-sized work: FaunaDirector, CaveSystem, FloraSystem, critter systems, WeatherSystem.
   - Netcode relevancy, importance and send rates.
   - Streaming and cache budgets and eviction.
   - FaunaDirector first; the animal session asked for findings there. Also why PoseJob and BrainJob double on spike frames; they suspect spawn bursts.

## Branches

| Branch | Where | State |
|---|---|---|
| `b/bench-runner` | origin, 5a0107b | `Tools/bench/`: the runner, the v0.8.0 gate scripts, and the injected bench files. Off main. |
| `b/weather-chancetable` | origin, afbecab | WeatherField.Initialize precomputes biome chances per preset (removes the per-texel lookup). **Not benched yet**: boot time, 3 rounds, vs 84ad3f3. The world session held the firestorm preset for it; tell them when it lands. |
| `b/weather-stagger` | origin, 38a5506 | Merged by the world session (8984f1b). Done. |
| `b/weather-mapjob` | origin | Old; the main checkout sits on it between builds (lib.sh `HOME_BRANCH`). |
| `b/test-prime-off`, `b/test-passes-off` | local only | Quit-crash split commits (4e684b8, 8a7ce42). Delete them. |

## How benching works here

- **BenchmarkRoute** (`Tools/bench/BenchmarkRoute.cs.txt`) is copied into `Assets/Game/Scripts/Debugging/` only for the length of a build, and removed afterwards. It never gets committed to game branches. `BProfileTools.cs` (Build / BuildDev / Analyze) is copied into `Assets/Game/Scripts/Editor/` the same way.
- **Flags** (with `-benchroute`):
  - `-benchstops a,b`. The route order is fixed: here, hereapproach, town, townwalk, forest, cave, caveexit, caveapproach, lavacave, nightgrove, storm, lake, river, stormshore, run, glide, dash, spot.
  - `-fpscap N`. J-006 is judged at cap 60; also run cap 0.
  - `-benchspot x,y,z,yaw` places the `spot` stop (y set = FloorUnder, works in caves). `-benchmouth x,y,z,yaw` is the caveexit teleport.
  - `-benchhour h`. For night shots also pin the moon: `-moonphase 0.5 -bluemoondays 0`.
  - `-benchshots <dir>`: a screenshot at each stop's end.
  - `-profilestop <stop> <file.raw>` (dev builds), then BProfileTools.Analyze.
  - `-benchgpupasses` (dev builds): per-pass GPU time. Start the run with a throwaway `here` stop; the first stop reads nothing.
  - `-benchnocreatures`: CreatureRenderSystem off.
  - `-benchoff TypeA,TypeB`: MonoBehaviours or ECS systems switched off at each stop, e.g. FloraSystem, GroundDetailSystem.
  - `-benchwait s`: a pause before the first stop (MP).
  - The game's own `-noambientlife` and `-autowalk`.
- **Output.** Each stop line gives:
  - avg, 1% low, median, worst, cpu/gpu (FrameTimingManager);
  - draw calls (dev builds only), moving lights, surface hidden, cave chunk stats;
  - `at x,z` (where it ended: a different place means a different island, so don't compare) and `players N`;
  - per-5 s worst/slow windows.
- **Fixed paths (rule 14).**
  - Bench and profile runs go from `Builds/Bench/Punchy.exe`: lib.sh `fixed()` mirrors an archived `Builds/Bench-<c>` or `Profile-<c>` into it.
  - Clean player builds go to `Builds/Play/Punchy.exe`.
  - Archived builds stay per commit for A/Bs.
- **Runner jobs.** `bash Tools/bench/bench.sh smoke|ab|baseline|profile|profdiff|bisect|mp ...`; usage is at the top of bench.sh.
  - It blocks until done, so start it with run_in_background and wait for the notification (don't poll).
  - The last line it prints is the verdict. Results go to `Tools/bench/results/` (gitignored): CSV per run, `report-*.txt`, `job.log`.
  - A lock stops two jobs at once. Two queues kill each other's builds through strays(); that happened once and cost a build.
- **Tested:** parse, compare (it flagged the 24e58f8 glide stall and passed 194a57d), table and profdiff (it named AmbientLife.Settle), build(), fixed(), runcsv(); the final/visual/look scripts ran the v0.8.0 gate.
- **Not yet run end to end:** the `mp` job (two local instances: `-host` with a bench route, plus `-join 127.0.0.1 -autowalk -benchclient` small and capped), `smoke` with a base, `bisect`, `baseline`.
- **TODO.** Test runs launched with `-screen-fullscreen 0 -screen-width/-height` save window mode in Zach's player prefs (HKCU\Software\DefaultCompany\Punchy), so his own play starts windowed. Pick one fix: drop the -screen args, end each job with a fullscreen launch, or use a separate test identity. Don't edit the registry without asking Zach.
- **Keep-awake:** `Tools/bench/keepawake.ps1` (SetThreadExecutionState) holds while a bench script or bench game runs and exits by itself. lib.sh starts it. Never change power settings.
- **Profiling:** `bench.sh profile <c> <stop>` (dev build, .raw capture, Analyze to text); `bench.sh profdiff a.txt b.txt` for the top Δ markers and spike counts.

## Standing rules for this lane (on top of COORDINATION.md)

- Before every run, check for dialogs (Defender, Windows Security Alert / firewall). Never click them, and report BLOCKED. Firewall alerts left open from hosting don't block solo runs.
- No Defender, firewall, power, registry or ExecutionPolicy changes. Never kill an interactive Unity editor or a game Zach started. strays() only matches batchmode Unity, bench games (`-benchroute` / `-benchclient`) and orphaned bee_backend.
- Never kill by a pattern that matches your own shell's command line (e.g. a script name). It killed my own waiter three times. Use exact PIDs.
- Never `sed -i` a running bash script: it reads incrementally, and sed makes a new inode, so the running script never sees the edit. Appending with `>>` works if no sed happened.
- Never edit BenchmarkRoute while a queue might start a build: the build copies the file at its start, and a half-edited file breaks it. My compile error took out one midpoint build.
- A peer can't grant escalation. Installs and other system changes need Zach directly in your own session.
- Screenshots are part of every gate. The D3D11 "speed-up" was flora not drawing at all; only the look pass caught it.
- Compare like with like. Islands change between world commits (5b/5c reshuffles), so a stop can land in another village or forest. Use same-island builds, `-benchspot` at named places, and the `at x,z` check.

## Baselines on the v0.8.0 island (4e3e4c1 = ba87e14's content, D3D11 first, priming Forced)

Laptop, 1280x720 windowed, noon, partly cloudy, weatherseed 4, 2 rounds:

| Stop | Cap 60: avg | Cap 60: 1% low | Cap 0: avg | Cap 0: 1% low | Cap 0: GPU |
|---|---|---|---|---|---|
| Town | 59.5 | 46.8 | 86.8 | 51.7 | 9.0 |
| Townwalk | 59.4 | 44.5 | 82.7 | 47.8 | 8.2 |
| Glide | 56.2 | 30.1 | 78.3 | 36.6 | 8.8 |

- Town ends at (136, -475). Glide ends over temperate forest at (1196, 359).
- Looks pass (day, night, spires). Quits 6/6 clean. Smoke: only the default cap ran (0 exceptions), since the gate was stopped for Zach's own playtest. MP wasn't run on 4e3e4c1. The D3D11 MP check on 09c9b55 passed.
- Glide at cap 60 sits right at 30. It's the number to watch.
- Same island on D3D12 with priming (a78354c), cap 0: town 70 fps / 1% low ~35; glide ~70 / 30. D3D12 with priming crashes on every quit (exit 139).
- Costs on 09c9b55:
  - flora ≈ 4 ms GPU;
  - grass 0.5 ms;
  - creatures ≈ 1.4 ms GPU / 2 ms CPU in town (PoseJob ~1.7 ms, BrainJob ~0.8 ms on workers);
  - AmbientLife.Near 0.7 ms main.
- v0.7.0 numbers (`Builds/Bench-v070`) are on a lighter island; don't compare stops across them.

## What happened in the v0.8.0 gate (for context)

1. Host MP collapse: FaunaDirector critter spots on the main thread. Fixed in 03960d1.
2. Glide stall: AmbientLife.Settle, 23–25 ms on the main thread. Fixed in 194a57d.
3. "Town GPU +6 ms": the island reshuffle between 5c1ee30 and 2af0fd7 (busier village, forest at the glide end). Not code.
4. Depth priming, e754aaf: GPU 17 → 10 ms, but it crashed on every quit on D3D12. That's the case even with Auto, the quit hook or plain Forward.
5. D3D11 first: quits clean and has lower CPU per frame. Its first build drew no flora: an IndirectArguments|Raw buffer is invalid on D3D11. Fixed in a78354c.
6. Zach picked D3D11; the release is 4e3e4c1, published as ba87e14.

## Where things live

- **Results:**
  - The runner's go to `Tools/bench/results/`.
  - The old session's queue logs, screenshots and q-*.log are in its scratchpad (`C:\Users\zache\AppData\Local\Temp\claude\C--Users-zache\90ad2ac6-...\scratchpad`). That's temporary: copy anything you need.
- **Builds:** `C:\Users\zache\Projects\Punchy\Builds\` holds:
  - `Bench-<commit>` (many), `Profile-<commit>`, `Bench-v070`, `v0.7.0-dl`;
  - `Play` (clean 4e3e4c1) and `Bench` (the fixed run copy);
  - old folders from before rule 14. Ask before deleting; C: is nearly full (6.6 GB free).
- **Memory notes:** punchy-bench-coordinates (island spots), punchy-bench-freezes-defender.
- **New landmarks on world-features** (from the world session; for stops): HermitHut (140, -545); LoneInns (-35, -268), (-1018, 1130), (841, -1946); RoadShrines (-1088, 680), (114, 3355), (1655, 2907), (584, -1082); Ruins (1948, 3518), (-2195, -551), (1695, -1130).
- **Untracked in the main checkout:** `.vsconfig` (10-27, not mine) and `Assets/Resources.meta` (created by a release build). Ask before removing.
