# Map editor (M-07, #260) - design note

Row 1 of M-07. Heightmap first; biome paint, place moves and road/river waypoints come later on the same file.
Rules 22 (world file stays editable) and 33 (one ground-edit system) apply.

## Goal

Load the baked island in the Unity editor, raise and lower ground with a brush, see it live, and save the
edits to a file that the game, builds and multiplayer all apply the same way.

## 1. Loading into an edit-mode scene

- An editor window (`MapEditorWindow`, Editor folder) opens a dedicated edit scene with the terrain subscene.
- The island comes from the island cache (W-140, `IslandCache.TryLoad`), not a fresh bake. A cache hit takes
  well under a second; a miss bakes once (about 14 s) and writes the file, as it does in play mode.
- Terrain shows through the normal chunk meshing (`TerrainSource` sampling), restricted to a view box around
  the scene camera so the editor never meshes the whole 9.2 km island at once.
- Row 2a (editor only, built): the preview is the window's own grid mesh over part of the island (base heights
  from `SampleHeight` on worker threads, plus the edits); a brush rewrites only the vertices it reached. Brushes
  are `MapBrush` assets (raise with a sign, smooth, flatten; radius, strength, falloff curve), so the set is data.
  Edits are offsets over the finished ground as the preview shows it; where the bake applies them is row 2b.
- The brush preview re-meshes only the chunks under the brush. Places (rivers, towns, roads) do not refit in
  the preview; that needs a re-bake (see 2), shown by a "Re-bake" button and a stale marker.

## 2. Edits file: a sparse delta layer

- One file per world: `IslandEdits` (binary `.bytes`, so a build can carry it as a TextAsset), saved next to the
  world file (W-168). Until the bake hook lands it lives in `Assets/Game/WorldEdits/` and is not committed.
- Content: a sparse set of 64 m tiles, each 32x32 half-float height deltas at 2 m spacing. Tiles don't overlap:
  sampling near a tile edge reads the neighbour's vertices, so there are no seams and no shared rows to keep in
  step. Only touched tiles exist; an untouched island is an empty file (stable hash).
- Brush strokes are flattened into the tiles on save (no op log), so file size scales with painted area only.
  An undo stack lives in the editor window, not in the file.
- Header: magic, format version, island version it was painted on, grid spacing and tile size; a SHA-256
  trailer is both the damage check and the content hash.

### Fixed point in the bake

The delta is added to the **base height**, after `IslandV3.Generate` and before `TerrainAuthoring.BuildPlaces`:

- `TerrainSource.ShapeHeight` (and so `BaseHeight`) gains `+ Edits.Sample(xz)`; the delta tiles are a blob on
  `TerrainSource` like `Island` and `Archipelago`.
- Rivers route over, and places, roads, ponds, scarps and settlement levelling all fit, the edited ground. This
  is what the card asks for: move a hill and the road follows it.
- Settlement levelling and landmark beds level toward fixed heights, so they still win inside their footprints.
  Edits there are flattened by design; the editor greys those areas out.
- Empty file = no blob = byte-identical bake, so Landmarks_V3 and Fauna_Fingerprint stay pinned (row 2 check).
- A non-empty file moves the landmark hashes, as any generator change would (hashes are measures, not gates).

Rejected: an overlay after places (like GrandFalls). Hashes would hold, but roads and towns would float or sink
on edited ground, which defeats the point.

## 3. W-174's ground-edit system

W-174 (#406, world lane) plans one ground-edit record (shape, target height or depth, falloff, noise, rock
exposure, priority) applied in one place in `TerrainSource`. Its row 1 has not started, so the API is not
settled. Proposal for the world lane:

- The map-editor delta becomes one record kind in W-174: "painted delta, sparse tiles", applied at the base
  layer, ahead of every place-driven edit record.
- The editor never writes its own sampling code; it only produces tiles. `TerrainSource` applies them through
  W-174's apply step.
- Until W-174 lands, row 2 hooks the single `ShapeHeight` add in a small partial file, so the later move into
  W-174 is one call site.

**Open question for the world lane:** does W-174 have a base-layer stage (before BuildPlaces), or are all its
records post-place? If only post-place, the painted delta stays a separate base input that W-174 reads from.

## 4. Edits orphaned by generator changes

- Edits are keyed by world position, so they survive generator changes, but they may land somewhere new: under
  a river that moved, inside a town that now levels them away, over a cave mouth.
- On bake, an `IslandEdits` check lists each tile that overlaps a settlement footprint, a landmark bed, a cave
  socket or a river channel, plus tiles painted on another island version. It logs `[map-edits]` warnings and
  shows them in the window. It never fails the bake.
- Later (place moves by stable id): an optional anchor per tile to a place id, so a stroke can follow its town.

## 5. Multiplayer

- Edits are baked into the island, so everything downstream (chunks, collision, places) agrees with no runtime
  sync.
- The edits file ships in the build with the island file (W-168) and is part of the island cache key.
- `WorldSync.Signature` adds the edits content hash, so a client with different edits is refused, the same as
  a different seed or island version.
- No edits travel over the network in this card. Host-sent edits (a client joining a modded host) is a later
  item, alongside sending the island version.

## 6. Load-time cost

- Play and builds: the cache key includes the edits hash. A cache hit costs the same as today (the tiles are a
  few kB per km^2 inside the cached blob). A changed edits file costs one fresh bake on first launch.
- Sampling adds one tile lookup per height sample; an absent tile is one read of a flat tile index grid (144x144
  for 9216 m) and returns 0.
- Editor: saving edits changes the key, so the next play mode re-bakes once (about 14 s). The window re-bakes
  only on "Re-bake" or save.
- No timings in this note: start time and sample cost go to BENCH-QUEUE (rule 21) once row 2 exists, against
  `[stream] world ready`.

## Rows

1. This note (stop for Zach's OK).
2. Editor window: height brush, live chunk preview, save `IslandEdits`. Check: Landmarks_V3 unchanged with an
   empty file; edited ground matches in play mode.
3. Play mode and build, solo and host+client; mismatched edits refused.

## Questions for Zach

- Refit (edits before places; roads and towns follow, hashes move) is the plan. OK, or keep places fixed?
- Brush set for row 2: raise/lower, smooth, flatten to height. Anything else first?
