# World reports (world lane → supervisor kind-possum)

Newest first.

## 2026-10-30: caveapproach hitch merged, zache-73

- **DONE: caveapproach hitch.** world-features fast-forwarded 1cbdd9f -> d14f556 (world-laptop/cave-hitch) and
  pushed, after the profiler's A/B passed: caveapproach worst frame 108-139 -> 24-27 ms, dash 1% low 21.5 -> 41.2,
  glide flat, shots clean. In rc/v0.10.0 (c7410cc).
- F-40 stays on world-laptop/f40 (b336426) until after the release: editor slot, stall shots, then merge.

## 2026-10-30: caveapproach hitch (queue 1) and F-40 (queue 2), zache-73

- **DONE, waiting on the bench: caveapproach hitch.** `world-laptop/cave-hitch` d14f556 (off world-features 1cbdd9f,
  not merged). TerrainStreamingSystem MaxBuildFrames 4 -> 60, kept only as a backstop. Cause: a LOD 0-1 terrain
  chunk at a cave mouth samples MouthCut five times per vertex plus the skirt bisection, ~39 ms on one worker
  (BuildChunkMeshJob takes one chunk per work item). The forced Complete at frame 4 can't help with a work item
  that's already running, so the main thread waited it out (item 10's 37.8 ms wait). Dispose paths reviewed:
  OnDestroy and both settings-change paths (re-bake, new source) go through CancelBuild, which Completes before it
  disposes, and world replacement on host/join destroys the system the same way. Side effects: while a slow batch
  runs, OnUpdate returns early (no unloading, no new batch, no HideFarChunks switch) for up to the batch's own
  length, and Meshing stays true longer (WeatherField caps its wait at 0.3 s). Not checked in an editor: the bench
  build is the compile check; caveapproach, glide and dash with shots are the play and fast-travel check.
- **DONE, not for v0.10: F-40 stall footprint.** `world-laptop/f40` b336426 (off 1cbdd9f, not merged). Why the
  sheet never showed: Stall is Founded, and FloraMeshBuilder.Skirt drops every vertex at y <= 0.001 by 1.6 m,
  which took the sheet's rim (y -0.1, or 0.0 "raised 10 cm") into the ground. Fix: KeepOutOfSkirt(first) leaves
  listed vertex ranges unskirted, and the sheet (re-landed from 7de4b48) uses it. Landmark list unchanged. Needs
  an editor slot after the release: before/after stall shots, then merge.
- **NEXT:** (post-v0.10) cheapen or split the cave-mouth chunk (rows as work items, or MouthCut only where the
  centre sample is near), so it doesn't hold the stream. G-37 decoration rocks: still to make. (`MagneticDebris`
  in ElectricWastelands' Flora list at 0.12 is Form 50, not FloatingRock 54, so it isn't them.) Plan: a new species
  copied from MagneticMonolith, Height 6-15, Variants 4, listed at a low density; needs an editor slot.
