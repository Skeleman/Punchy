# World lane state

Updated 2026-11-02, W-30 (archipelago) PAUSED by the supervisor for Zach's demo. Branch world-features @ 0bdc707,
unchanged: nothing committed for W-30. No build and no player run made.

## W-30 work so far (saved as Punchy-world/Temp/w30-archipelago.patch, also left uncommitted in the tree)
- New Assets/Game/Scripts/Terrain/Island/Archipelago.cs (+ .meta). It is an overlay like GrandFalls: baked in
  IslandV2System after BuildPlaces and GrandFalls, v2 only, so landmarks, caves and settlements never see it.
  - Build: surveys rays every 3 deg for the dune coast with the longest sea run to the coast square
    (WorldSize/2 + CoastWidth/2 - 60). The coast probe expects about 30-40 deg, near (3200, 2240). It then lays an arc
    of up to 20 islets from 260 m offshore: 2-4 big ones (110-290 m wide) near shore, mid islets 32-84 m, then rocks
    7-16 m, plus satellite rocks. Sandbars (crest -0.3 to -0.7 m) join near islands that are under 170 m apart.
    It uses its own seed stream (Scramble(Seed ^ 0x5A17A1E5)).
  - Shape: only raises the sea floor (fades out where the ground is above sea - 0.5). The profile is a beach, then
    a dune crest, then a 2.6 m shelf, then a fall to the floor.
  - Surface: turned to Dunes over islets and shelves, in SampleBiomes on the map branch. Shape weights unchanged.
  - Logs an "[archipelago]" line: count, sandbars, coast point, extent, and each islet's coords, width and crest.
- TerrainSource: Archipelago field, equality, SampleBiomes, SampleHeight and SampleGround (applied first, so sea
  rules see it). IslandV2: reset, build, Shared copy.
- DebugControls: `-archipelago off|on` swaps the field (before/after in one run). Each shot log line now adds the
  draw calls, batches and k tris from ProfilerRecorder.
- Compiles clean in the editor (port 7801). Editor play mode runs v1 (102 / 51EA6463 unchanged); v2 not yet run.

## Left for W-30
- v2 run: read the [archipelago] log line, the v2 hash (126 / 053C63D5) and whether the site and count look sane.
- One build, then one shot list (time ~11, -weather Clear, -nohud):
  - aerial ~250 m over the dune coast;
  - a beach on a big island looking at the mainland;
  - before/after stats lines with -archipelago off/on;
  - end with -quit.
- 800 px shots into PunchyMilestones/world/w30, view them, commit, update docs/agents/world.md.

## Open (older)
- v2 bridges per island not counted. Cobble cost not benched. From W-28b: a taller dry-dock cave berg. From W-29:
  slanted sheet beside the main falls, snowy levees, plain gorge walls.

## Hashes
- v2 126 / 053C63D5, caves 5D90D9CFC5326976 (not re-read for W-30).
- v1 102 / 51EA6463 (re-read in editor play mode with the W-30 code), caves 47C703C0E17F77CB.

## Notes
- Editor log is Punchy-world/Temp/editor-w04.log. Eval: `unity command --port 7801 script eval_file Temp/x.cs`
  (path positional).
- Shots: `Builds/Dev/Punchy.exe [-island v1] -shotlist <file> -shotdir <dir> -logFile <file>`.
- Scratch: Punchy-world/Temp/w30_* (coast probe w30_coast.cs).
