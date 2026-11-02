# Performance rules

Read before adding jobs, per-frame work or anything that samples the terrain.

- Heavy background jobs crowd out the terrain's own jobs and cause hitches; keep per-frame placement budgets small
  (`FloraSystem.CellsPerFrame`).
- `TerrainSource` sampling is the hot path: terrain meshing samples it about six times per vertex, and plants,
  animals and ambience call it constantly (some from plain C#, not Burst). Anything that's slow to work out per
  region belongs in a baked table (see `BiomeCellTableBlob`), not in the per-sample code.
- Never `Schedule(...).Complete()` heavy work in the same frame: the main thread ends up doing it. Pick results up
  a frame or two later, as `TerrainStreamingSystem` and `FloraSystem` do.
- Editor entity names are limited to a few thousand distinct strings; don't give streamed entities unique names.

## Learned in October 2026 (from the lane handoffs)

- A static field placed between `[BurstCompile]` and its struct is a compile error (the attribute lands on the field).
- Burst's `Debug.Log` formats only G/D/X (no `F1`).
- `math.hash(uintN)` has no avalanche: scramble it before comparing per-name random draws.
