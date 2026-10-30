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
8. When animal-behaviours next pushes: townwalk cap-60 capture split by the new BrainJob ProfilerMarkers (Brain.Hunter/Predator/Chaser/Alarm/Playmate/Needs/Fidget/Decide/Advance/EaseGait) against a4521a2. Glacier lake stop (item 7) waits on the world session.
9. Item 7 unblocked: world-features (after 4ec2cab) has 0 water walls. New lake look stop: x 1328, z 1730 (ground 117.0, lake 116.8), yaw -90 (west over the lake). No ducks there (Mountains); for item 6 swimmers use a lake in FoggyMarsh, FungalForest, Grassland or Temperate.
10. Re-run caveapproach on world-features 807ee9f (HideFarChunks batched since 000ef06). If the ~110 ms worst frame remains, capture that frame's top markers (Cave Stream vs Terrain HideFarChunks vs the underground transition).
11. Host/join on animal-behaviours ea3bdad: the TrySpawn join fix (counts resized in GatherPlayers); watch the host log for IndexOutOfRange right after the welcome. Item 8 (BrainJob marker split) can use ea3bdad too.
12. The next MP check, on animal-behaviours after 6d70335 (wire: Climbing bool, plus a mantle rim byte coming): a remote climber shows upright against the trunk or wall, a remote mantle keeps its hands on the rim, and there are no join IndexOutOfRange errors.
13. AFTER the v0.10 release: Brain.Advance split capture. Townwalk cap 60, `bench.sh profile` on wip/brain-sample 7bd0768
    (animal-behaviours) and wip/brain-sample-e4010f2 6cfedaf (its twin on e4010f2), then profdiff. Read: Brain.Moving
    callsPerFrame = movers; Brain.Sample per call; Brain.Phase; Advance self = heading + Accelerate (zache-db's note in
    REPORTS-animal.md). A failed build is the animal lane's compile error: send it the log.
14. AFTER item 6 and the world lane's slot: item 13 again, island-controlled. 6cfedaf vs 7bd0768 crosses the island
    change (G-18 roads 1e53043, F-22, F-30 only in 7bd0768), and Brain.Sample's code is identical in both, so the +30%
    per call may be layout. Marker commits (profiler-only, never merge): wip/brain-blocked 32ec6ff (on 7bd0768) and
    wip/brain-blocked-e4010f2 91f8957 (on 6cfedaf), adding Brain.Blocked round Walled → FloraPlacement.Blocked (the
    world lane's suspect: it re-runs Place() for nearby town plots and props on every call). Plan: townwalk cap 60
    on both (Sample minus Blocked per call vs Blocked per call), plus a control at a -benchspot forest spot away from
    roads and landmarks, shot on both builds to confirm the same trees. Same Sample per call at the control spot
    means the townwalk gap is layout.
