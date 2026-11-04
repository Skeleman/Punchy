# Profiler lane: machine and files (laptop)

## Machine
- i7-7700HQ, Optimus GTX, Unity 6000.5.8f1, D3D11. C: had 180 GB free on 2026-11-02; ask Zach before deleting builds.
- `gh` is logged in (as Skeleman). The keep-awake (SetThreadExecutionState only) may still be running from the old
  laptop supervisor; it doesn't stop sleep on lid close. Never change power settings.

## Paths
- Main checkout `Projects/Punchy`: builds happen there in detached HEAD, then it returns to `b/weather-mapjob`
  (lib.sh HOME_BRANCH). Untracked `.vsconfig` and `Assets/Resources.meta` aren't ours; leave them.
- Runner: worktree `Projects/Punchy-wt`, branch `b/bench-runner` (c7018e0: stops work on island v2 too), in
  `Tools/bench/`:
  - `bench.sh` (smoke | ab | baseline | profile | profdiff | bisect | mp);
  - `lib.sh` (build, run, runcsv, profile, compare, table, fixed, dialogs, keepawake);
  - `BenchmarkRoute.cs.txt` and `BProfileTools.cs`, copied into the project only for a build.
  - Results in `Tools/bench/results/` (gitignored); the lock is `results/.lock`.
- Coordination worktree `Projects/Punchy-coordination` (branch `coordination`): BENCH-QUEUE.md, BENCH-REPORTS.md,
  COSTS/, lanes/.
- Per-slot scripts lived in the session scratchpad (slot.sh..slot4.sh, swim.sh, mp2.sh, play.sh): re-create from
  the same pattern (take the lock, keepawake, build, ABBA, print tables) if they're gone.

## Archived builds (`Builds/`)
- 2026-11-02 a cleanup removed every build folder by mistake (a keep-list filter that never matched); rebuilt the
  same morning: Bench-4eea658 / Profile-4eea658 (v0.11.0), Bench-bc89606 (world-features before the cave-chunk,
  worm and blocked work; the A side of many checks), Bench-8d83102, Bench-b5e9715 / Profile-b5e9715, Bench-e3ab4af /
  Profile-e3ab4af, then Bench-0cc3ed7 / Profile-0cc3ed7 (main) and Profile-ed767e0 (A-26).
- 2026-11-03 also present: Bench-41a9259, Bench-a19bc79, Bench-762c16a / Profile-762c16a (main, item 11).
- Gone, rebuild on demand (`build <sha> Build Bench-<sha>`, ~5-7 min each): 21f1193, a66c520, b870008 (Z-30 grain),
  3976d17 (32-slot worm marks), 247c690, 256e6a9, d60e085, 2de3029, b70dbfa, 0c637a9, 3d1893d, 793f103; dev builds
  3481353 / 74d8148 (Brain.Blocked markers, on wip/brain-blocked*). No `Play` or release zips: v0.11.0 and earlier are
  on GitHub releases.
- Before deleting builds: ask Zach, print the exact list and its count, and check the keep names are absent from it.
- Archive folders are for reading results only: copy a build into `Builds/Bench` before launching it.
- Local-only branches in the main checkout: bench/worm-merge, bench/worm-mask, bench/wake-mask. Profiler-only marker
  branches on origin (never merge): wip/brain-blocked*, wip/brain-sample*.
