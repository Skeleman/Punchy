# Profiler lane: state (2026-11-03, handed to a new account)

The laptop is **profiler only** (since 2026-11-02). The supervisor is the main account on the main PC.
Backlog: `BENCH-QUEUE.md` (top first) and `gh issue list -R Skeleman/Punchy -l lane:profiler`.
Released: v0.11.0 = 4eea658. main = 27bf8c1 (PR #77; animal lane A-45 is in main, not in world-features).

## First session on a new account
1. Read this file, then `FILES.md`, `TESTING.md`, `lanes/shared/report.md`, `lanes/shared/git.md`.
2. Check: `gh auth status` (repo is public: no machine paths in issues or comments); `git pull --rebase` here;
   `results/.lock` absent in `Punchy-wt/Tools/bench/results/` (if present, read `.lock/job` and ask Zach).
3. Hooks: `sh hooks/install.sh <folder outside the trees>` (BENCH-QUEUE setup) if telemetry isn't installed.
4. Nothing queued on 2026-11-03 after item 12. Stay idle until Zach or the supervisor adds work; don't poll.

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
