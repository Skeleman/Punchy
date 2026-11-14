# World lane state (2026-11-14)

Worktree D:/Unity/Punchy-world. Read the card and REPORTS-world.md's newest entries for detail.

## Branches

- `world/waterfall-sheets` 54fcdf64: R-21 row 4 (one fall builder). Look accepted; merges after BENCH job 74.
- `water/truth` a8fe44e5: W-153 rows 1, 1b, 2 (water truth probe; still water last). Suite 41/41.
- `world/scarps` feab6870: W-155 row 1 (two test scarps, ground cut and rock skin; shared hooks in 11b2753a). Suite 40/40. Bench ask in the handoff.

## Pins

- Landmarks 117 689D5348, fauna fingerprint 6A9E28E5 (unchanged by R-21 row 4, W-153 row 2 and W-155 row 1).
- Water truth ceiling (WorldTests.cs): 80, 0, 0, 0, 10, 31, 0, 988, 42, 31, 959, 47, 4. May only go down.

## Queue

1. W-155 row 1b (Zach 11-14: break the stripes, varied ledges), then row 2 with his colour note (row 1 done: bake 89 ms, aim under 2 s). Row 5 leftovers: no ground clip beyond LOD2, skin proud at the canyon cap on LOD1/2, river cut bank does not read at 400 m.
2. W-153 row 2b: keep more hillside swamp pools (36 of 322 cells hold one now) without raising wall or step counts.
3. W-153 rows 3-6 with W-155 row 4. Carried in: water walls beside falls 22 and 67 and in front of 46 and 51; fall 13's notch; cave falls without lip dip and foot ripples; the fine sheet reading FallShape; 109 fall and landmark head-pool walls; retake fall 46 and 51 fronts and the snowpeak landmark fall.
4. W-156 (audit 11-13: far clip restore, nearest cave water first).
4b. W-157 (Zach 11-14: taller hanging lanterns, stone lantern columns).
5. W-141 fix row, W-154 (row 2 now also asks for finer normals: rough, coarse dark crust, smooth bright veins; see batches/W-154.md), R-22 row 2.

## Tools and gotchas

- Untracked probes in Assets/Temp/Editor (never commit): WorldProbe.cs, W153Probe.cs. Water truth: `TestHooks.WaterTruthCounts()`.
- TerrainSource.cs, TerrainAuthoring.cs and IslandV2.cs are CRLF: edit by exact byte replace, check `git ls-files --eol`.
- Scripts don't recompile in play mode: stop play first. A code change re-bakes the island (2-3 min).
- A long eval (over about 100 s) times out but keeps running in the editor: use Parallel.For in a probe.
