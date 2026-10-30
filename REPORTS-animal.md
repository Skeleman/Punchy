# Animal reports (animal lane → supervisor kind-possum)

Newest first.

## 2026-10-30 02:10: task 2, Brain.Advance split: DONE code-only (not merged)
- `wip/brain-sample` 7bd0768 (off animal-behaviours) and its twin `wip/brain-sample-e4010f2` 6cfedaf (never merge).
- Inside Brain.Advance: Brain.Sample (ground/water/cave/wall sampling), Brain.Phase (gait step), Brain.Moving (empty
  mark per moving creature: callsPerFrame = movers). Advance self = the rest.
- Reading: more Moving calls with Sample/call flat = more movers (Accelerate); Sample/call up = dearer sampling.
- NEXT: waiting on the profiler's run of wip/test-hooks 0019e8a; then merge it to animal-behaviours.

## 2026-10-30 01:55: task 1, test hooks: DONE code-only, wip/test-hooks 0019e8a (not merged)
- `-place tree:<species>` now faces the trunk. New `-place ledge`: to the nearest settlement, then (once props' tops
  load) on level ground 1.5 m in front of the nearest rim the body can mantle (about 1-1.8 m for a human), facing it.
- `-autowalk` with either: 12 s at the trunk or over the rim (jumping near a rim), 8 s back, repeating every 20 s.
- Both log `DebugControls: climb test: ...` with the trunk/rim position and ready `-goto`/`-camlook` client flags.
- Not compiled or run by me (no editor slot); the profiler builds and runs `-body Monkey -place tree:Oak -autowalk`
  and `-place ledge -autowalk`. Merge to animal-behaviours after both pass.
- NEXT: task 2 (Brain.Sample marker plus a moving count), code-only.
