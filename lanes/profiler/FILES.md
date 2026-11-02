# Profiler lane: machine and files (laptop)

## Machine
- i7-7700HQ, Optimus GTX, Unity 6000.5.8f1, D3D11. C: has about 16 GB free; ask Zach before deleting builds.
- `gh` is logged in (as Skeleman). The keep-awake (SetThreadExecutionState only) may still be running from the old
  laptop supervisor; it doesn't stop sleep on lid close. Never change power settings.

## Paths
- Main checkout `Projects/Punchy`: builds happen there in detached HEAD, then it returns to `b/weather-mapjob`
  (lib.sh HOME_BRANCH). Untracked `.vsconfig` and `Assets/Resources.meta` aren't ours; leave them.
- Runner: worktree `Projects/Punchy-wt`, branch `b/bench-runner` (db3c1f2, adds `-benchday <day type>`), in
  `Tools/bench/`:
  - `bench.sh` (smoke | ab | baseline | profile | profdiff | bisect | mp);
  - `lib.sh` (build, run, runcsv, profile, compare, table, fixed, dialogs, keepawake);
  - `BenchmarkRoute.cs.txt` and `BProfileTools.cs`, copied into the project only for a build.
  - Results in `Tools/bench/results/` (gitignored); the lock is `results/.lock`.
- Coordination worktree `Projects/Punchy-coordination` (branch `coordination`): BENCH-QUEUE.md, BENCH-REPORTS.md,
  COSTS/, lanes/.
- Per-slot scripts lived in the session scratchpad (slot.sh..slot4.sh, swim.sh, mp2.sh, play.sh): re-create from
  the same pattern (take the lock, keepawake, build, ABBA, print tables) if they're gone.

## Archived builds worth keeping (`Builds/`)
- Release candidates: `Zip-v0.11-cand2` (v0.11.0 as published, 4eea658), `Zip-v0.11-cand` (21f1193).
- Bench-4eea658 / Bench-21f1193 / Profile-21f1193 (v0.11), Bench-a66c520, Bench-b870008 (Z-30 grain).
- Bench-3976d17 / Profile-3976d17 (z-p2, 32-slot worm marks), Bench-247c690.
- Bench-bc89606: world-features before the cave-chunk, worm and blocked work; the A side of many checks.
- Bench-256e6a9 (cave-chunk), Bench-d60e085 (unmasked worm marks), Bench-2de3029 (worm mask), Bench-b70dbfa (wake
  mask), Bench-0c637a9 (gfx sweep), Bench-3d1893d / Bench-793f103 (swimmer wakes).
- Profile-3481353 / Profile-74d8148 (Brain.Blocked markers), Profile-256e6a9.
- Archive folders are for reading results only: copy a build into `Builds/Bench` before launching it.
- Local-only branches in the main checkout: bench/worm-merge, bench/worm-mask, bench/wake-mask. Profiler-only marker
  branches on origin (never merge): wip/brain-blocked*, wip/brain-sample*.
