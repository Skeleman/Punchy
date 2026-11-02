# World lane state

Updated 2026-11-02 after W-28. Branch world-features @ 10a5ee1 (pushed). One build (Builds/Dev).

## Done in W-28 (icebergs)
- Icebergs in `Terrain/Icebergs` (Iceberg.cs: IcebergBaking + IcebergShape; IcebergSystem.cs: meshes).
  - Scenery from their own stream (seed ^ 0x1CEB3A65). A blob on TerrainSource, baked for both islands.
  - Placement: a 40 m grid. Sea is reached from the map border (so no rivers or lakes). Distance to the nearest
    cold land (Tundra or Glacier) must be under 1400 m and closer than any warm land. The "Ocean" beach strip
    counts as neutral shore for 320 m. Settlements are kept clear by 350 m + berg size.
  - Big bergs 200-1400 m out, 20-60 m across, 5-20 m high, at least 150 m apart. Chunks and floes from
    40 m out, fading away from the shore.
- v2: big 6, chunks 56, floes 38 off cold-coast km cells -5,1 -4,1 -2,3 -2,4 -1,4 (the west/north-west Cold realm).
- v1: east cold coast (km 3,2 4,1 4,2): big 1, chunks 8, floes 1.
- Cave berg: the big berg with the largest r*h, enlarged to at least r 18 / h 13. Its mouth faces the shore.
  - v2: berg (-4332, 1739), mouth (-4306, 1733), chamber (-4337, 1.2, 1740).
  - v1: berg (4226, 2354), chamber (4230, 1.2, 2358).
  - The tunnel winds through 4 nodes into a 6 m dome, 6.5 m high. Blue emissive ice, a soft point light, built in the berg mesh.
- Collision: IcebergShape.Wall/Deck feed TerrainSource.TowerWall/Deck (the player already calls both).
  Big bergs have a ramp slope on the shore side; elsewhere the sides are steep walls.
- Walk test (editor, v2): 922 steps, from swimming 5 m outside the mouth to the chamber and back. 0 blocked. The highest
  step was 1.45 m (swim to the tunnel floor).
- Render cost: one draw per 1024 m region. v2: 5 draws, 6232 tris; cave 1 draw, 1026 tris. v1: 3 draws, 2140 tris.
- No bob or drift (the bergs are fixed). The underwater part shows only near the surface (the water shader is opaque deeper down).

## Open
- Chamber: a small gap at the tunnel joint shows sky or sand. The look is flat (no real translucency or caustics).
- W-27 tower steps: Floor may ignore FloorUnder's tower floor unless inCave or Deck is set. Not checked.
- F2 hosting not covered in a build. The summit is a plain slab.
- The card quotes old hashes (v1 101/E128CB1D, cave D531293D622E785A). The current ones are below; none changed.

## Hashes
- v2 126 / 053C63D5, caves 5D90D9CFC5326976.
- v1 102 / 51EA6463, caves 47C703C0E17F77CB.

## Notes
- Shots: PunchyMilestones/world/w28 (800 px), raw shots in w28/raw.
- Scratch: Punchy-world/Temp/w28_*. Player log w28_player.log. Walk test: Temp/w28_walk.cs.
- `unity command script` does not exist: run builds with `unity command eval "$(cat Temp/x.cs)"`.
- The world editor logs to Punchy-world/Temp/editor-w04.log. Run `unity command eval` from bash with single quotes.
