# REPORTS-materials

Materials lane reports, newest last. Designs and handoffs for W-166 (the one material library) go here.

## W-166 rock surface r2 plan (#336), 11-17: scarp walls read the Stone table

Scope (Zach 11-17): one rule set per stone for every bare rock wall: cave walls, cliff scarps, river notches.
Row 1 makes `MaterialLibrary.Stone` (by `StoneKind`) general; caves are its first reader. Row 2 waits for the
coordinator's OK after the world lane's river rows land (it is in the scarp and TerrainSource files now).

- **Choosing the stone:** the ground material under the scarp, falling back to its biome. A `StoneKind` field on
  `BiomeDefinition` (default Plain) is the fallback; where the ground layer names a rock surface, that wins. Picked
  once per scarp at bake, from the rim's midpoint, so a scarp is one stone along its length (beds still vary).
- **Where it is stored:** `Scarp` (ScarpSet.cs) gains a `Stone` byte, set in `ScarpBake.Style` beside the kind's
  proportions.
- **Shape (TerrainSource shape pass, `ScarpSet.Shape`):** the layout-safe columns first: Grain scales crack size,
  Blockiness the bed push and setback steps (jointed blocks vs rounded beds), RibSpacing the gully spacing.
  Roughness and Jaggedness may scale Push and Crack later; they move the face, so that step re-pins the hashes and
  tells the profiler.
- **Surface (ScarpPaint, W-162 rock walls):** Detail and DetailScale drive the face's fine relief, the same columns
  the cave shader reads, so a granite cliff and a granite cave match up close. Climbing (bare rock grades) may read
  Blockiness for hold density in a later row.
- **Notches:** river notches are RiverCut scarps, so they follow once scarps read the table.

## W-166 row 1: cave walls read MaterialLibrary.Stone (#336), done 11-17
- Branch materials/cave-surface: cf7acd9c (table, untuned: cave sdf 3D2D78E06B03B800 and style table equal to before), 9cf9ca10 (tuning).
- `StoneKind` + general `MaterialLibrary.StoneRow`/`Stone` table; CaveStyle names a Stone; Cave.shader fine detail fades by ScreenSize ppm.
- Tuned granite, sandstone, tuff (layout-safe columns only); Limestone has no caves on this island, so tuff (Fungal) was tuned instead. Only those caves' sdf changed; node counts equal, NaN 0.
- Landmarks_V3 and the 3 cave tests pass. Shots: PunchyMilestones/W-166/cave/before and after (Granite, Sandstone, Fungal, same frames).
- Row 2 (scarps, notches) plan above; waits for the OK.
