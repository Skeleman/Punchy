# Profiling (rules 21, 23)

## Only the profiler profiles (rule 21)
- Lane agents never measure frame times, ms tables, bench runs or RenderDoc captures, whatever an older card says.
  Counts you get anyway (triangles, draw calls, index counts, memory from a script) are fine.
- A row that needs a cost check ends its handoff with a BENCH request: branch, commit, spots (`-goto` coords), arms
  (main vs branch, flags) and the pass line. The supervisor queues it in BENCH-QUEUE.md.
- Benches run uncapped (`-fpscap 0`) unless the card asks for a cap. Bench spots are fixed, named places; after a
  world move (`world-data.md`) old baselines no longer compare.
- **Main-PC bench agent (J-045, temporary):** tree Punchy-release, builds Bench-A and Bench-B. It runs only
  branch-vs-main jobs whose pass line is "no slower than main" and which are not near a budget, with the laptop's
  method (`lanes/profiler/scripts`: ABBA, Profile builds, 1920x1080, uncapped, 300 frames), and only while no other
  editor or player runs (the HOLD file, `unity.md`). Pass: within noise or under about 2% of frame ms; otherwise
  the job goes to the laptop. Reports are marked "main PC, provisional". Laptop only: budget and preset decisions,
  water, lights, clouds, two-machine tests, LOD reach.
- **Profiler's own fixes:** it may fix perf, crash and stall bugs it found and measured, in any file, on a
  `b/fix-*` branch from current main, when the file is not held by a running card, the fix is small (no features,
  no redesign) and it ships with before and after numbers. The supervisor reviews and merges.

## Targets (rule 23, J-044)
The supervisor writes every pass line from this. Reference: the profiler laptop, 1920x1080, Profile builds for GPU.

| Preset | 1% low floor |
|---|---|
| Low | 50 fps (budget) |
| Medium (default) | 30 fps (budget) |
| High | 25 fps (soft: a miss is a note) |
| Ultra | none, report only |

- A feature on at Medium: at most +0.3 ms GPU, CPU within noise, at its worst spot. Over about +0.5 ms it gets
  cheaper, justifies itself or moves up a preset. On only at High: at most +1 ms. Ultra-only: priced, never held,
  but free at Medium and below.
- Hitches, any preset: no new frame over 2x median, none over 100 ms on a walk or flight. Load and first-arrival
  spikes are tracked apart.
- Refactors and no-look-change work: within noise of main at every spot.
- A miss is a note and a fix card. A merge is held only for a big, repeatable miss (over about 1 ms at Medium, a
  new hitch, a preset under its floor).
- A feature moves up a preset through preset values (view, detail, shadows, DetailPolicy thresholds) or its own
  runtime setting. ProjectSettings, QualitySettings and the URP asset change only through the supervisor.
- The profiler reports any NaN count above 0 on a bench route, with the view (rule 24).
