# World lane state

Updated 2026-11-02 after W-29. Branch world-features @ d9ce83c (pushed). One build (Builds/Dev).

## Done in W-29 (grand falls, v2 only)
- Survey: option A (mountain stream into the central lake) beats B (lake spilling out).
  - The lake sits at 25.3 m (depth 11 m), so B can drop at most ~25 m to the sea (best rim SE, near (757,1265)).
  - A: from (316,905) at 166 m to the north-west shore, ~140 m total. Nothing on the path (no village, cave, road
    or river). The Tors (448,1360) are dodged by bending the lower river east.
- `Terrain/Rivers/GrandFalls.cs`: a blob on TerrainSource, baked in IslandV2 after BuildPlaces and shared by Shared.ApplyTo.
  - The path is a polyline. Its water is a staircase (a fall = two nodes 1.5 m apart, 3 m for the main drop), so the surface never breaks.
  - Ground: channel bed, banks raised or a gorge cut (bank width 8 m + 0.4 x height made up), round plunge pools past each foot.
  - Hooked into SampleHeight and SampleGround after landmarks and before ponds and Settle. Water, flow and rapids come from it.
- Result: 548 m long, 166.0 -> 25.3 m (140.8 m), 5 tiers:
  - 5.0 m at (322,920); MAIN 72.5 m at (334,954), lip 147.5, pool r 20.
  - Then 10.3 m at (383,1081), 5.5 m at (405,1141), 5.5 m at (441,1221).
  - Flow 1.2-4.0 m/s on reaches (steepest 49.5%), 0.6 m/s in pools.
- Effects (LandmarkEffectSystem.GrandTier): curtains (3 side by side at the main drop), lip white water, pool foam,
  and two big mist plumes at the main drop. Seen from the full view range.
- Sound: FallVoice reach now 60 m at 14 m drops, rising to 180 m at 70 m+ (volume 1). Player log: 3-4 sources, max 180 m.

## Open
- The water mesh draws a slanted sheet across the main drop beside the curtains (wider than the curtains).
- Raised banks on the upper plateau look like snow levees. The gorge walls are smooth and untextured-looking.
- One reach chute is 49.5% steep (rapids, no fall). The site is fixed coordinates for seed 8121.
- Not run on v1 (v1 never builds the falls: TerrainAuthoring is untouched).
- W-28 leftovers, not done: the gap at the ice-cave tunnel joint (v2 berg -4332,1739), and the W-27 tower steps
  (Floor ignores FloorUnder's tower floor unless inCave or Deck is set).

## Hashes (unchanged in W-29)
- v2 126 / 053C63D5, caves 5D90D9CFC5326976.
- v1 102 / 51EA6463, caves 47C703C0E17F77CB.

## Notes
- Shots: PunchyMilestones/world/w29 (800 px), raw shots in w29/raw.
- Scratch: Punchy-world/Temp/w29_*. Player log w29_player.log. Survey: Temp/w29_survey.txt, w29_q.txt.
- The world editor logs to Punchy-world/Temp/editor-w04.log. Run `unity command eval "$(cat Temp/x.cs)"` from bash.
- The player quits by itself after a -shotlist (-quit added too).
