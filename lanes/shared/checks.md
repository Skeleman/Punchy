# Checks, shots and the test suite (rules 8, 12, 12a, 12b, 19)

Check each feature with nothing around that could hide it or be mistaken for it. Use numbers where you can; take
shots only for things that have to be seen.

## Batch (rule 8)
Implement related items together, evaluate them in one shot-list session, send one report and one compare sheet.
Separate commits per item. Split only for risk or a decision that is Zach's.

## Stage
- Clear weather, fixed time: `-weather Clear -time 12` (or what the feature needs), `-weatherseed 4`.
- HUD and debug panel off: `-nohud`, `DebugControls.ShowReadout = false`.
- Avatar out of frame and out of the camera's line (the reveal cone cuts holes): `-camlook` or `-camrel`; a side
  view is `-goto x,z` plus `-camlook`.
- Remove what competes with the subject. Stand in an open spot (lane TESTING.md) or spawn only the subject
  (`-spawn <Species>:1`).
- Before and after: same camera, seed, time and coats. Light dark scenes.

## One run
- Every view in one shot list, one player session (`-shotlist`); the last line quits the player.
- Log numbers from the game (probe flags, summary lines); read the log once at the end.
- Wait for the run's own "done" line; no polling in short sleeps.

## Shots (rule 12)
- 16:9 at the true aspect (`cam.aspect`). Frame tightly, 3-8 m away, whole subject in frame; crop for fine detail.
- At most 3 per report, 800 px long edge; a sheet counts as one, 1200 px max. Never reuse a shot across reports.
- No images for changes you can't see (culling, audio, network, cost): give numbers.
- **Who looks (12a):** check your own shots at about 480 px. Send the supervisor one 800 px final only for a
  finished, visible feature; for fixes and interim steps send a written check (looked for, saw, caveats).
- **Gallery (12b):** full-size extras for Zach go in `D:/Unity/PunchyMilestones/<card>/gallery/`, at most about 40.
  Never open them yourself; name the folder in the report.
- After a shader or `.hlsl` edit, run `AssetDatabase.Refresh` before shots, or they show the old shader.

## Suite (rule 19)
- Shot, plan-map and look tests are `[Explicit]`. The default suite holds only fast checks with numbers; a new
  default test runs under about 30 s.
- Re-run the full suite after your last code change and quote its line.
- A card's cost limits are counted on the same spot before and after (triangles, draws, memory; no timing).
- A shot that can't be made to show the change: write UNVERIFIED with the reason and move on. No retake loops;
  Zach judges the look in the playtest.
