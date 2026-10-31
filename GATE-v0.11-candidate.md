# v0.11 candidate gate (2026-10-31, task session; NOT tagged, NOT released)

**Candidate:** `cand/v0.11` 21f119309b4b11bcbb1b9297a23dfa30f095a667 = world-features b39522b + animal-behaviours
77c03e9 (clean merge, pushed). **Base:** v0.10.0 519fb5b. Same island: every smoke and look stop ends at the same x,z
in both builds. Flora differs (v0.11 placement changes): the glide end and lake have more trees in view.
Results: Punchy-wt/Tools/bench/results (g11-*, q-g11*, mp-21f1193-*, g11-21f1193-climb/shots).

| Item | Result | Key numbers (candidate vs v0.10.0) |
|---|---|---|
| Release build (ReleaseBuild, BuildOptions.None) | PASS | Succeeded, product Punchy, 0 error CS, 0 shader errors; Builds/Play = 21f1193 (.from) |
| Solo boot/quit ×2 (no -screen-*) | PASS | exit 0 both, Direct3D 11.0, not development, 0 exceptions, 0 NaN-bounds |
| Smoke townwalk (ABBA, cap 60 / 0) | PASS | 1% low 37.8 vs 39.2 / 46.9 vs 50.8 (within noise), 0 exc |
| Smoke glide cap 60 | PASS (borderline J-006, as v0.10) | 1% low 29.7 vs 29.6 |
| Smoke glide cap 0 | FLAG (not blocking for J-006; for Zach) | smoke 1% low 19.6 vs 35.3 (r1 16.4, r2 22.8; worst 66/82 ms vs 37/34). Follow-up ABBA below: 31.3 vs 35.6 |
| Looks with shots (day 6 stops, night 2, spire) | PASS | all 9 stops render on both builds, exit 0, 0 exc; night grove glows, spire arcs show |
| MP host/join (bench.sh mp pattern) | PASS | players 2 in every joined row, 0 exc, 0 IOOR. Joined 1% low town / townwalk 25.1 / 20.5 (v0.10 gate's c7410cc: 26.8 / 26.8, another day); same-session ABBA below: 23.2 / 22.8 vs v0.10 22.6 / 19.5 |
| MP climb (client -shotlist) | PASS | players 2; remote Monkey seen on the Oak trunk (climb-09) and at its base; 0 exc, 0 IOOR either side |
| NaN bounds / exceptions grep | PASS | every candidate log (release, smoke, looks, MP, climb): Mesh bounds 0, NaN-bounds 0, Exception 0 |
| Zip (`python release/pack.py`, .from held out) | PASS | Punchy-v0.11.0-windows.zip: 237 files, 49,468,639 bytes (47 MB), SHA-256 847f35c1cb4319fa8966122a98442eb957b2237b31577480c0cc237774ad7337; backslash False, donotship False, exe True, no .from. Local only at Projects/Punchy/Builds/Zip-v0.11-cand/, not uploaded |

## Follow-up 1: glide cap 0, ABBA over 4 builds (2 rounds each, same island)
| Build | 1% low r1 / r2 | worst ms r1 / r2 | 5 s windows over 50 ms |
|---|---|---|---|
| 21f1193 candidate | 35.2 / 27.4 | 34.7 / 59.2 | 1 of 14 (plus 3 of 14 in the smoke) |
| a66c520 (b870008 + animal 77c03e9, no world slot 3) | 36.9 / 37.5 | 33.7 / 35.0 | 0 |
| c7bb5f3 (world slot 3 alone, = b39522b tree) | 37.6 / 37.2 | 34.8 / 37.3 | 0 |
| 519fb5b v0.10.0 | 34.9 / 36.4 | 51.8 / 34.9 | 0 (one 52 ms frame) |
- Reading: the candidate had 50-80 ms windows in 3 of its 4 cap-0 glide runs today (55 slow frames in one 5 s window
  once); the other three builds had none in 10 runs. Neither branch alone shows it, so it's either the merged tree
  (world slot 3 x animal slot 7) or noise that happened to land on one build. Averages are fine (77.7 vs 79.4 fps),
  cap 60 is unchanged, no exceptions. Not fixed: it needs a profile capture of glide on a dev build of 21f1193
  (`bench.sh profile 21f1193 glide 0`), which didn't fit in this slot.
- glide in the day look run (cap 0, single): 31.1 vs 30.3, no spike.

## Follow-up 2: MP host with a local client joined, cap 60, ABBA over 3 builds (2 rounds)
| Build | town 1% low | townwalk 1% low | townwalk avg | host cpu ms | townwalk slow frames |
|---|---|---|---|---|---|
| 21f1193 candidate | 23.2 | 22.8 | 46.6 | 21.5 | 62 |
| a66c520 | 22.2 | 22.9 | 45.2 | 22.1 | 99 |
| 519fb5b v0.10.0 | 22.6 | 19.5 | 42.0 | 23.8 | 132 |
- The joined-vs-solo drop in the gate (townwalk 33.8 -> 20.5) is the two games sharing this laptop; v0.10.0 drops as far
  today. No MP regression. (Two games on one laptop is below J-006 for every build; that's the setup, not the game.)

## Verdict
Everything passes except one flag: **cap-0 glide spikes on the merged candidate** (50-80 ms windows in 3 of 4 runs;
none on either parent or v0.10 in 10 runs). Cap 60 (the J-006 measure) is unchanged at 29.7 vs 29.6. My read: not a
release blocker, but worth one profile capture before Zach ships, or ship and watch. No fixes were made on cand/v0.11.

## Not done / for Zach
- No tag, no GitHub release. Draft notes: RELEASE-NOTES-v0.11-draft.md.
- Default graphics preset still Zach's call (ships Medium).
- The zip is local only (Builds/Zip-v0.11-cand, gitignored); the matching player is Builds/Play.
