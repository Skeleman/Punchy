# World lane state (2026-11-14)

Worktree D:/Unity/Punchy-world. Read the card and REPORTS-world.md's newest entries for detail.

## Branches

- `world/waterfall-sheets` 54fcdf64: R-21 row 4 (one fall builder). Look accepted; merges after BENCH job 74.
- `water/truth` a8fe44e5: W-153 rows 1, 1b, 2 (water truth probe; still water last). Suite 41/41.
- `world/scarps` 13fbd5cf (pushed): W-155 rows 1, 1b, 2 parts 1 and 2, 2b (debris, plant room via one FloraPlacement call, bare margin in GroundDetailSystem, toe/ends/top hand-over, no stripes). Float probe 0, pins unchanged. BENCH job 85.

## Pins

- Landmarks 117 689D5348, fauna fingerprint 6A9E28E5 (unchanged by R-21 row 4, W-153 row 2 and W-155 row 1).
- Water truth ceiling (WorldTests.cs): 80, 0, 0, 0, 10, 31, 0, 988, 42, 31, 959, 47, 4. May only go down.

## Queue

1. W-153 row 2b: keep more hillside swamp pools (36 of 322 cells hold one now) without raising wall or step counts.
2. W-153 rows 3-6 with W-155 row 4. Carried in: water walls beside falls 22 and 67 and in front of 46 and 51; fall 13's notch; cave falls without lip dip and foot ripples; the fine sheet reading FallShape; 109 fall and landmark head-pool walls; retake fall 46 and 51 fronts and the snowpeak landmark fall.
3. W-156 (audit 11-13: far clip restore, nearest cave water first).
3b. W-157 (Zach 11-14: taller hanging lanterns, stone lantern columns).
3b2. W-164 (Zach 11-14: gardens and crop plots sit level on slopes, no sinking; small; #334).
3c. W-159 (Zach 11-14: river flow streaks become normal and vertex ripples, no white overlay; small, may go before 4b).
3c2. W-160 (Zach 11-14: shorelines wander: noise lift and lapping on water vertices, ragged shore fade; after W-159).
3c3. W-161 (Zach 11-14: elf towns have no wood; signs, fences, docks and props in stone or metal; #326).
3d. W-158 (Zach 11-14: darkened seams where built faces meet, beacon stair first).
3e. W-162 (Zach 11-14: cliffs and caves share rock surface, climb grades and ground blend; after W-155 merges; #327).
3f. W-163 (Zach 11-14: original procedural wood grain for all planks and timber; static strangler roots on swamp trunks and some cave walls; no copied Shadertoy code; #330).
4. W-141 fix row, W-154 (row 2 now also asks for finer normals: rough, coarse dark crust, smooth bright veins; see batches/W-154.md), R-22 row 2.

## Tools and gotchas

- Untracked probes in Assets/Temp/Editor (never commit): WorldProbe.cs, W153Probe.cs. Water truth: `TestHooks.WaterTruthCounts()`.
- TerrainSource.cs, TerrainAuthoring.cs and IslandV2.cs are CRLF: edit by exact byte replace, check `git ls-files --eol`.
- Scripts don't recompile in play mode: stop play first. A code change re-bakes the island (2-3 min).
- A long eval (over about 100 s) times out but keeps running in the editor: use Parallel.For in a probe.
- W-155 row 5 leftovers: no ground clip beyond LOD2, skin proud at the canyon cap on LOD1/2, river cut bank does not read at 400 m; matching beds across a canyon untested.
- A second suite run in one editor session fails WorldTests.Place_PropFindsAndFramesWharf (identical line); restart or trust run 1.
