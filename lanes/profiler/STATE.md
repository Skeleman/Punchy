# Profiler lane: state (2026-11-03, handed to a new account)

The laptop is **profiler only** (since 2026-11-02). The supervisor is the main account on the main PC.
Backlog: `BENCH-QUEUE.md` (top first) and `gh issue list -R Skeleman/Punchy -l lane:profiler`.
Released: v0.11.0 = 4eea658. main = 8b4bf19 (A-51, PR #101).

## First session on a new account
1. Read this file, then `FILES.md`, `TESTING.md`, `lanes/shared/report.md`, `lanes/shared/git.md`.
2. Check: `gh auth status` (repo is public: no machine paths in issues or comments); `git pull --rebase` here;
   `results/.lock` absent in `Punchy-wt/Tools/bench/results/` (if present, read `.lock/job` and ask Zach).
3. Hooks: `sh hooks/install.sh <folder outside the trees>` (BENCH-QUEUE setup) if telemetry isn't installed.
4. Jobs 17-26 reported 2026-11-04 (24c: far shadow fix confirmed; 26: self-fix b/fix-netstatus pushed for review). Self-fix rule: COORDINATION.md File ownership (b/fix-* from current main, file not busy, small, before/after numbers; supervisor merges). Jobs 27 (1699574) and 28 (6f139d0) reported: NetMenu hitches gone (#125); open: fishing village brain cost (#132, animal lane), startup bake (#102), fast-flight streaming, -place prop: broken (#137; use -goto or -warp town N). Job 29 (#132 before/after) reported. Job 30 (creature system refactor audit) reported in REPORTS-profiler.md. Job 31 (GPU split, fv1 + v3 night town) reported there: flora + its shadows are the fv1 GPU frame, water ~0, no point lights at night; draw counters still unreadable. Job 32 reported: #102 frame = main-thread bake (12.0 s v2 / 14.3 s v3; batfolk, rivers, generate top, world lane); W-102 baseline from capture counters (shadow casters 340 -> 17 with flora off at fv1). Job 33 (Zach direct): shadow option 1/2 shots + metrics (q33.sh) and the BenchTune live panel (-benchtune, F9-F12) on b/bench-runner. Pending: when W-107 (FloraShadowOptions switches) reaches main, BenchTune rows bound (fc31412; supervisor merging). Job 33 reported (bdda6319). Stalls traced to the runner (FrameTimingManager captured up to 3x a frame by BenchTune): fix on b/fix-stall-hang (889e3e89) for review; keep counting 0.5 s hitches in sweeps. dump.ps1 ready, unused. Next: re-bench when W-107 card holes / still overlap / cache land. Standing order: keep finding frames over 33 ms when idle.

## How work arrives (the supervisor messages directly with SendMessage, or adds BENCH-QUEUE items)
- Pull `coordination` at the start of every job and before every push (`git pull --rebase` on rejection).
- Report one entry per job at the top of `BENCH-REPORTS.md`: the file is **CRLF**; insert right after
  "Newest first.\r\n\r\n" (`scripts/` has no helper; a 6-line python byte replace works). Include
  `Cost: <calls> calls, peak <k> context, $<equiv>`.
- After every job: `python tools/usage_log.py --machine laptop`, commit `COSTS/laptop.csv`, push.
- Comment one line on the job's issue with the commit. **Agents never close issues**: the supervisor closes them
  after merge plus Zach's OK.
- If a job needs Zach (an install, a dialog, a design call), say so in the report and the issue, and stop.

## Rule: uncapped (Zach, 2026-11-03)
All profiles and benches run uncapped (no frame cap, vsync off) unless a card asks for a cap.

## Unity on the laptop
- Only Unity user here. Still take `results/.lock` for every run; one editor or player at a time (MP pair excepted).
- Launch only from `Builds/Bench` (`fixed <build>` copies it there). Never `-screen-*` on a real Punchy build.
- No Defender/firewall/power/registry/ExecutionPolicy changes. Kill only by exact PID. Never click dialogs.
- Ask Zach before deleting builds; list the exact items and count, and check no keep name is in it.

## Open threads
- **Item 15**: Burst audit; all runtime jobs are Burst, main thread is the limit (workers 7-16% busy). Re-profile
  with `scripts/q15.sh` after the animal/world sync fixes land.
- **Item 16**: W-73 leak fix confirmed. Open: intermittent player crash on the 2nd host in one session (Burst statics
  init), v2 2/3 runs, also before W-73; repro `scripts/q16b.sh` (needs a Bench build with NetCycle.cs.txt copied in).
- Runner 3f76120: the cave stop settles until CaveSystem.Settled (30 s max).
- Item 13: hut-cave camera gap closed on main.
- **Item 12 (#79)**, box collision: no measurable cost (b28ee7b -> 52b4af5). Re-run `scripts/q12.sh` + `q12b.sh`
  when W-67b lands (change the commits). The hut cave camera gap vs main is A-45, missing on world-features.
- **Item 11 (79be014)**, slow caves: v2 cave CaveArm up to 5 ms/frame along walls (main 14.1 ms); cave field
  4.1 us/sample on v2 vs 1.6 on v1; mesh draw not a factor. Re-run `scripts/q11c.sh` (walk test) after a #54 or
  field-cache fix: the v2 east leg is the number to beat. Comments on #54 and #63.
- Item 10 (a19bc79): v2-default host loses its own player during the double launch bake; v1 MP fine.
- TraceSystem: 2.1 ms main at v2 caveapproach (#55); re-profile townwalk when the animal lane fixes it.
- ECS review issues #54-#64 (2026-11-02) are open for the lanes; the profiler re-measures when they land.
- Gfx default is **Medium** (Zach, 2026-11-01): glide 1% low ~30 at cap 60. No action unless asked.
- ~500 ms stalls = an open dialog (firewall/Defender) until shown otherwise; check `dialogs`, read the full line.
- #31 houses lit wrong: fixed on the main PC (Zach, 2026-11-03); a laptop town shot confirms it if asked.
- GPU per-pass timings (`-benchgpupasses`) report nothing on this laptop; only FrameTime.GPU totals.

## Modules
- `FILES.md`: machine, paths, runner, builds. `TESTING.md`: flags, gotchas, baselines.
- `scripts/`: item 11 scripts (q11 bench+profiles, q11b leftovers, q11c cave walk), as patterns for new jobs.
  They source the runner's `lib.sh`; never assign its variable names (see TESTING.md).

- Crash tests (Zach, 2026-11-04): stop at the first crash and report it with its stack; one crash is enough to say a fix failed. Only a clean result needs the full set of runs/re-hosts.
