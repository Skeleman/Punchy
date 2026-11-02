# World lane state

Updated 2026-11-02 after W-25. Branch world-features @ a19bc79 (pushed). One build (Builds/Dev).

## Done in W-25
- Rows 1-2 (#23, stepped paddies): hillside swamp pools are now irregular paddy cells (`TerrainSource.Terrace`).
  - Cells about 15 m across (warped, jittered grid), whatever the slope. Each cell is level at its centre's
    `BaseHeight` (cut uphill, filled downhill). Banks stand 0.3 m above the higher neighbour.
  - Tall risers widen up to 6 m so they aren't walls.
  - W-24's contour bands were about 6 m wide at slope 0.1 and 1-2 m at the steep pond (-2260,569 has slope 0.48).
  - PoisonSwamp box (+-480 m at -2400,500), W-24 -> W-25:
    - pools (16 m): 15 -> 15, wet cells 33 -> 33 (pond outline unchanged)
    - water levels (steps): 119 -> 89
    - level pools (2 m grid): 858 -> 173; mean pool 9 -> 32 m2 (edge fragments pull the mean down)
    - water area: 1906 -> 1399 points of 2 m
    - transect at -2260,569: pools 7 m wide between ~5-7 m risers (was 1-2 m bands)
  - Build shots: from 30 m up looking down the slope, it's a clear staircase of paddies with dark mud banks.
    The 8 m side shot shows stepped pools and banks.

## Open
- The 8 m shot looking straight up the slope only shows a grass riser (a framing miss; the side shot covers it).
- A few cells at the pond edge read as square pits (a cut cell whose rim rises to the natural ground).
  Fading `inside` where the cut is large would fix it, if Zach wants it.
- Overflow trickles between paddies: not done (optional on the card).
- Some pools look pale/white from high up: the sky reflects on flat water. That isn't a terrain issue.

## Hashes (confirmed in the editor; v2 also in the W-25 build log)
v2 125 / AFA9CC63, caves D531293D622E785A; v1 101 / E128CB1D. Unchanged.

## Notes
- Shots: PunchyMilestones/world/w25 (800 px), raw shots and shots.txt in w25/raw.
- Scratch: Punchy-world/Temp/w25_*.cs. w25_measure.cs gives the box numbers plus transects (run it in play mode).
- Building from the open editor: `script eval_file` of Temp/w25_build.cs with `--timeout 600` (about 60 s).
- The -2347,542 pond centroid is not inside water. For framing, use -2260,569 (uphill direction 0.80,0.60).
