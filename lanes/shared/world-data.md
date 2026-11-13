# World data and world changes (rule 22)

## The layout may change (Zach, 11-13)
The island is a test bed; the final game will have a different layout. Systems and procedural generation come
first, and dramatic world changes are welcome. Zach's one concern is the cost to profiling and test scripts.

A row that moves terrain, rivers, lakes, landmarks, placement or the fingerprint:
1. tells the supervisor first (rule 11; the supervisor decides, Zach is not asked);
2. re-pins the hashes and gives old and new in the handoff;
3. fixes, in the same row, every test script, probe and fixed shot or bench vantage point it broke, and lists them;
4. gives the supervisor one BENCH-QUEUE line saying which baselines no longer compare.

Hashes are measures, not gates. Since 5b landmark seeds are stable per type: a new landmark type takes only its
own spots.

## Placement is data (rule 22, #260)
One day the island is generated once at design time, saved to a file, hand-edited and loaded. New and changed
world code stays compatible with that:
- (a) Every placed thing (landmark, cave, dungeon, tunnel, road or river waypoint, settlement, building, colossus
  spot, hermitage, lone NPC, fence, sign, light, scarp) leaves its bake step as a plain record (kind, position,
  rotation, size, params) with a stable id, in a blob or list; not only as a mesh, entity or side effect.
- (b) Later steps and runtime systems read those records. They never re-run an earlier placement or its RNG.
- (c) Placement RNG runs only in the bake. Runtime may scatter visuals (flora, small details, chunk meshes) from
  records, ground and seed; nothing gameplay is placed at runtime.
- (d) A bake step depends only on its inputs (seed, settings, earlier records): no editor, scene or frame state.

Old code that breaks this is fine until touched; list what you notice in your handoff.

`TerrainSource` is the single source of truth for the ground. Anything that can go underground uses
`FloorUnder(float3)`, not `SampleHeight`.
