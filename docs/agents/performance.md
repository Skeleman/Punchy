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
- A per-creature managed `TerrainSource.SampleHeight` on the main thread costs ~16 us each in the Mono player (2.1 ms for 130 creatures in `CreatureRenderSystem`'s cave cull); run such loops as a Burst job (`.Run()` is fine).
- The same goes for `ThirdPersonCamera`'s cave spring arm (up to 30+ `IsRock` calls a frame: 0.7-2.3 ms managed, 0.02-0.07 ms as a Burst job). A struct job outside `using Unity.Jobs;` needs `Unity.Jobs.IJobExtensions.Run(job)`: `job.Run()` binds to `IJobEntityExtensions` and fails.
- `-perflog` on a shot-list line logs frame/CPU/GPU ms, cave chunks submitted/in view, probe and `IsRock` cost (frame timing needs `PlayerSettings.enableFrameTimingStats` in that build). `-oldcave` turns the cave room cull and the Burst arm off for A/B runs; `-gocave x,z` / `-gocave #n` drops the player on a cave floor.
- GPU A/Bs: pass `-display 1920x1080,windowed` and `-fpscap 0`, and check every shot's size. `-screen-width` in exclusive fullscreen can fall back to the desktop's 4K on one run and not the next, which looks like a large GPU difference. `-oldfloralod` draws flora by the old per-layer distances (refactor 3.5c).
