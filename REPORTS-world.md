# World reports (world lane → supervisor kind-possum)

Newest first.

## 2026-10-30: F-40 and G-37 in the post-release slot, zache-73. Unity closed

Checked with bench-runner player builds (Bench-<hash>, built with lib.sh `build`, ~5 min each) and framed shots
(`-place <landmark> -benchstops here -cam/-camlook`), not an editor. No Unity is running now; Projects\Punchy is back on
b/weather-mapjob.
- **DONE: G-37 decoration rocks.** world-features 2310533 -> 6464ce7 (fast-forward, pushed): MagneticDrift, a
  FloatingRock species (MagneticMonolith copied: 6-15 m, 4 variants, seed 337) in ElectricWastelands' flora at 0.03
  per 100 m2. At 0.01 a whole wastelands view held one, lost among the small Debris clusters (MagneticDebris is form
  Debris, not FloatingRock). At 0.03 the view at -1096,-3946 shows a big cluster with shards and arcs, a slab and a
  shard over their scorched mounds. Same view, same build otherwise: avg 59.8 / 1% low 57.8 before, 59.8 / 57.9
  after. Only the biome's Flora list changed: landmark list unchanged (101, E128CB1D).
- **NOT MERGED: F-40 stall footprint.** world-laptop/f40 677bd3b (pushed). With the skirt fix alone (b336426) the
  sheet still didn't show: same OgreCamp stall (-355,-4079, camera from the fire's side), before and after looked
  identical. Second cause: the sheet sloped from -0.1 m at its rim to +0.03 m in the middle, so only the middle
  quarter (under the counter) ever cleared the ground. 677bd3b raises it (rim +0.01, middle +0.08). Now it shows,
  but as a flat, untextured brown slab with straight edges in front of the counter, half buried by the dune
  relief. It doesn't read as trodden ground. Proposal for v0.11: darken the ground in the terrain shader instead (a
  trodden mask round stall props, like roads' ground), or a soft-edged decal. Your call or Zach's; the branch stays
  unmerged.
- NEXT: queue item 4 (worm ground ripples, wip/world-worms) once you hand out the next slot, or the F-40 redesign
  if you pick one. The cave-mouth chunk follow-up stays NEXT for v0.11.

## 2026-10-30: G-29 arch NaN fix brought in, zache-73

- **DONE:** cherry-picked rc/v0.10.0 519fb5b (arch bow sin clamped at 0) onto world-features as 2310533, pushed; file
  still CRLF. Scanned for other Pow(sin/cos, fractional): only FloraGenerator :622 (sin at a segment's midpoint,
  always inside (0, pi)), safe; the rest use integer powers or non-negative bases.

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
