# Bench queue (supervisor → kind-possum)

Since J-041 kind-possum runs on a different Claude account, so cross-session messages may not reach it.
The supervisor adds requests here; kind-possum pulls before each job and reports in BENCH-REPORTS.md
(newest first, one short entry per queue: gate results and real regressions only).

## Setup (once, after the account switch)
- Pull `coordination` and run `sh hooks/install.sh <fixed folder outside your trees>` (J-041 commit telemetry).
  Make one test commit on a b/ branch and check ~/.punchy-telemetry/commits.csv gets a row.

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
5. Later: when world-features gains `-island v2` (batch W-05), smoke `-island v2` vs default on the same build.
