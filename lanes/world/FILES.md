# World lane: key files (under Assets/Game/Scripts unless noted)

- **Flora models:** Flora/FloraGenerator.cs (form builders: Stall, Bench, Fountain, Smithy, RailFence, Signpost;
  mushrooms ~line 1150; gabled roofs ~3150/3220; Founded(); ToMesh flexibility). Flora/FloraMeshBuilder.cs (vertex
  marks: Bark + 0.08 strata, 0.25 seams, 0.4 veins, 0.02 wood; Skirt; Sheet/Box/Beam/Tube/Blob; Cloth).
- **Placement:** Flora/FloraPlacement.cs (Place(..., footprint), Blocked, RockAt, OnBuiltPlot, OnRoad, RockTint,
  FloraRules).
- **Flora draw:** Flora/FloraSystem.cs (layers, ScaleRanges, basins = fountain water, bolts); Shaders/Flora.shader
  (Frag: weathering, wood grain, ember slivers, grounding), Flora.hlsl (wind, cloth).
- **Landmarks:** Terrain/Landmarks/LandmarkData.cs (Ground, TroddenGround, pools), LandmarkBaking.cs, LandmarkType.cs.
- **Settlements/roads:** Terrain/Settlements/RoadBaking.cs (ClearPlots), RoadsideBaking.cs, SettlementData.cs.
  Streets and squares repaint ground after landmarks: anything that must show in a town goes after them.
- **Terrain:** TerrainSource.cs (SampleSurface, MouthCut), TerrainStreamingSystem.cs (ApplyViewDistance,
  MaxBuildFrames), BuildChunkMeshJob.cs. Caves/CaveSystem.cs (Look(), MakePoolMesh, plants/drips), CaveBaking.cs
  (PlaceStreams, RimHolds).
- **Settings/UI:** Settings/GraphicsSettings.cs (View/Detail/Shadows, F5 panel, -gfx/-gfxset), Overlay.cs;
  UI/ (MenuNav, MenuNav.TextEntry), InputGlyphs.
- **Sky/water:** Shaders/Sky.hlsl (cloud march, shooting stars), Water.shader (queue Transparent-10),
  LandmarkPlume.shader + LandmarkEffects.hlsl (bubbles).
- **Plan and log:** WORLD.md in the repo root.
