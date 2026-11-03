# Profiler lane: state (2026-11-02)

The laptop is **profiler only** from 2026-11-02 02:00. The supervisor is the main account on the main PC.
Session: zache-11 (laptop). Backlog: `BENCH-QUEUE.md` (top first) and `gh issue list -l lane:profiler`.
Released: v0.11.0 = cand/v0.11 4eea658 (world b39522b + animal c810406).

## How work arrives (no SendMessage across machines)
- Pull `coordination` at the start of every job and after every push. Take the top item of `BENCH-QUEUE.md`,
  or an open `lane:profiler` issue. The supervisor (or Zach in this window) is the only source of jobs.
- Report: one entry per job in `BENCH-REPORTS.md` (newest first, format in `lanes/shared/report.md`, plus a
  `Cost:` line), then a one-line comment on the job's issue with the commit. Commit to `coordination` and stop.
- Standing rule (BENCH-QUEUE.md): after every job, run `python tools/usage_log.py --machine laptop` and commit
  `COSTS/laptop.csv`.
- If a job needs Zach (an install, a dialog, a design call), say so in the report and the issue, and stop.
- If nothing is queued, stay idle. Don't poll in a loop; Zach or a scheduled check-in starts the next look.

## Unity on the laptop
- The profiler is now the only Unity user here, so there are no slots to wait for. Still take `results/.lock`
  for every run, and never run two editors or two players at once (except an MP host+client pair).
- Launch builds only from `Builds/Bench` (or Builds/Play for ReleaseBuild). An exe started from an archive folder
  (`Builds/bench-<sha>`) raised a Windows firewall prompt on 2026-10-31 that stalled every run for hours.

## Open threads
- Gfx default: Zach chose **Medium** (2026-11-01). Medium glide 1% low ~30 at cap 60 (just under J-006, as in
  v0.10); the in-between view 0.8/detail 0.75/shadows 0.8 gives ~34-35; Low ~57. No action unless asked.
- ~500 ms single-frame stalls: seen 2026-10-31 with the firewall dialog open; none in the v0.11 candidate-2 gate.
  If they return, check `dialogs` (read the full line) and other processes before blaming the code.
- Worm marks at 32 slots: +0.2 ms, inside the noise; a one-worm frame outside the Glacier would be tighter.
- Wakes: re-check with a camera over open water. Item 15 cave-chunk: a per-chunk timing log would settle it.
- 2026-11-02 queue items 1-5 done (BENCH-REPORTS): world 8d83102 and animal b5e9715 smokes pass; Dunes puffs
  +0.9 ms GPU, night sky +0.35; remote roll seen (#48).
- #31 houses lit wrong: Zach (2026-11-03) says it was found and fixed on the main PC. Profiler stands down; a
  laptop town shot on current main confirms it if asked (flags in the 574167c report). Laptop plays at 1280x720.
- TraceSystem 0.05 -> 0.40 ms median at townwalk since Z-50 (c0b26df), reported on #6. Re-profile after a fix.
- Builds/ was wiped by mistake on 2026-11-02 and partly rebuilt; see FILES.md for what exists and what to rebuild.

## Likely next jobs
- 2026-11-02 afternoon items 7-9 done (BENCH-REPORTS): bench stops fixed for v2 (b/bench-runner c7018e0); main v2 cave
  CPU 1.8x v1 (camera LateUpdate +1.8 ms), v2 ready 9.6-10.1 s; review of bat folk and folk chats. Item 10 (a19bc79): v2-default host loses its own player during the double launch bake (Timeout); v1 MP fine.
- Item 6 done: v2 cave stop CreatureRenderSystem 10.5 ms main thread, v2 townwalk misses the town (BENCH-REPORTS).
- Item 11 done (79be014): slow v2 caves = CaveArm + v2 cave field (4.1 us/sample vs 1.6), not the mesh; re-run q11c walk after a #54 fix.
- Re-profile townwalk when the animal lane fixes TraceSystem; #48 remote flying/swimming descent if asked.

## Modules
- `lanes/profiler/FILES.md`: machine, paths, runner, archived builds.
- `lanes/profiler/TESTING.md`: flags, gotchas, baselines.
- `lanes/shared/report.md`, `lanes/shared/git.md`.
