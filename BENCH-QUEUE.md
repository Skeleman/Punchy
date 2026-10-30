# Bench queue (supervisor → kind-possum)

Since J-041 kind-possum runs on a different Claude account, so cross-session messages may not reach it.
The supervisor adds requests here; kind-possum pulls before each job and reports in BENCH-REPORTS.md
(newest first, one short entry per queue: gate results and real regressions only).

## Setup (once, after the account switch)
- Pull `coordination` and run `sh hooks/install.sh <fixed folder outside your trees>` (J-041 commit telemetry).
  Make one test commit on a b/ branch and check ~/.punchy-telemetry/commits.csv gets a row.

## Queue (top first)
1. NOW, TOP PRIORITY (blocks the v0.9 release; not in BENCH-REPORTS yet as of 21:23): v0.9 gate on the final rc/v0.9.0 39f5558 (smoke vs v0.8.0 ba87e14, then host/join). Report PASS/FAIL in BENCH-REPORTS.md.
2. Re-queue what the pause cleared: smoke world-features 28d51e9 vs 0c0198a plus the cave/caveapproach ab;
   townwalk cap-60 profiles on e4010f2 and animal-behaviours a4521a2 (the townwalk cost fix) plus profdiff.
3. Capture to confirm audit item 1 (BrainJob/PoseJob back to back) at townwalk and town arrival on animal-behaviours a4521a2.
4. After the v0.9 gate: host/join on world-features (latest) for the cached WorldSync/NetMenu queries. Note: v0.10 roads move ~20 roadside landmarks (hash D82C9D7F to 7AFF6C9F, 104 to 100), so compare v0.10 to v0.9 by absolute checks, or rebase the A/B on the new island.
5. Re-bench townwalk cap 60 on animal-behaviours 6d0fbff vs a4521a2 (EaseGait trimmed: feet compensation within 30 m, jitter skip).
6. Bench a lake with swimmers (ducks) on world-features 793f103 vs 3d1893d: water shader swimmer loop (up to 16 per pixel within 12 m).
7. Move the lake look stop: (1103,132,1540) is now dry shore (F-22 removed the hanging water). Use camera ~(1110,146,1545) looking at (1135,141,1570); open water at (1125,141.4,1560).
